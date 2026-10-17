using System.Collections.Immutable;
using Microsoft.Extensions.Logging;

namespace DomainBlocks.EventStore.Subscriptions;

/// <summary>
/// Reads a store's event log in order and hands each event to every attached observer. The tail starts on the first
/// attach and stops on the last detach. A tail that has failed is replaced on the next attach.
/// </summary>
/// <remarks>
/// <para>
/// A tail starts at the end of the log and reads it through an
/// <see cref="EventLogCursor{TEvent,TSourceEvent,TStreamId,TStreamPos,TLogPos}"/>, which waits for events to be
/// appended and retries transient errors, so observers never miss an event. Any other error stops the tail and is
/// passed to every observer.
/// </para>
/// <para>
/// An async gate serializes the tail's lifecycle: starting a session, counting its attachments, and stopping it, so the
/// next session starts only after the previous one has stopped. The read loop never takes the gate. A session's
/// observers are held in an immutable array that is replaced by compare-and-swap, never changed in place, so the read
/// loop never blocks, even when it detaches an observer that threw or records a failure.
/// </para>
/// </remarks>
internal sealed class EventLogTail<TEvent, TSourceEvent, TStreamId, TStreamPos, TLogPos> : IAsyncDisposable
    where TEvent : notnull
    where TSourceEvent : IReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>
    where TStreamId : notnull
    where TStreamPos : struct
    where TLogPos : struct, IComparable<TLogPos>
{
    private readonly Func<IEventSubscriptionSource<TSourceEvent, TStreamId, TStreamPos, TLogPos>> _sourceFactory;
    private readonly EventLogTailOptions _options;
    private readonly ILogger? _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _disposeCts = new();
    private Session? _session;
    private int _disposed;

    public EventLogTail(
        Func<IEventSubscriptionSource<TSourceEvent, TStreamId, TStreamPos, TLogPos>> sourceFactory,
        EventLogTailOptions? options = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(sourceFactory);

        _sourceFactory = sourceFactory;
        _options = options ?? EventLogTailOptions.Default;
        _logger = logger;
    }

    /// <summary>
    /// Attaches the observer, and starts the tail if it is not running. This returns once the tail has read the end of
    /// the log, and the observer gets every event that the tail reads after it is attached. Disposing the result
    /// detaches the observer.
    /// </summary>
    public async Task<IAsyncDisposable> AttachAsync(
        ITailObserver<TSourceEvent> observer,
        string correlationId = "unknown",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observer);

        // Disposing the tail cancels an attach that is still waiting, so disposal never waits behind a start that keeps
        // retrying.
        using var attachCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);

        try
        {
            return await AttachCoreAsync(observer, correlationId, attachCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested &&
                                                 !cancellationToken.IsCancellationRequested)
        {
            throw new ObjectDisposedException(GetType().FullName);
        }
    }

    /// <summary>
    /// Stops the tail and rejects further attachments. Observers that are still attached are not notified.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await _disposeCts.CancelAsync().ConfigureAwait(false);
        await _gate.WaitAsync().ConfigureAwait(false);

        try
        {
            if (_session is { } session)
            {
                _session = null;
                await session.StopAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IAsyncDisposable> AttachCoreAsync(
        ITailObserver<TSourceEvent> observer,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

            if (_session is null || _session.IsCompleted)
            {
                // A session that has failed may still be finishing. It has already disposed of its source, so waiting
                // for it only keeps its last log lines ahead of the next session.
                if (_session is not null)
                    await _session.Stopped.ConfigureAwait(false);

                _session = new Session(this);
            }

            var session = _session;

            try
            {
                await session.Started.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested && session.RefCount == 0)
            {
                // Nobody else is attached, so stop the tail rather than leave it running.
                _session = null;
                await session.StopAsync().ConfigureAwait(false);
                throw;
            }

            var sessionAttachment = session.Attach(observer, correlationId);
            session.RefCount++;

            return new Attachment(() => DetachAsync(session, sessionAttachment));
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task DetachAsync(Session session, IDisposable sessionAttachment)
    {
        sessionAttachment.Dispose();

        await _gate.WaitAsync().ConfigureAwait(false);

        try
        {
            if (--session.RefCount > 0)
                return;

            if (ReferenceEquals(_session, session))
                _session = null;

            // This runs within the gate, so the next session cannot start until this one has stopped, and a store never
            // has two tails reading its log at once.
            await session.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// One session of the tail, from its start to its stop or failure.
    /// </summary>
    private sealed class Session
    {
        private readonly EventLogTail<TEvent, TSourceEvent, TStreamId, TStreamPos, TLogPos> _tail;
        private readonly ILogger? _logger;
        private readonly string _tailId = CorrelationId.ReserveGenerated();
        private readonly CancellationTokenSource _stopCts = new();
        private readonly TaskCompletionSource _startedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private SessionState _state = new([]);
        private int _stopping;

        public Session(EventLogTail<TEvent, TSourceEvent, TStreamId, TStreamPos, TLogPos> tail)
        {
            _tail = tail;
            _logger = tail._logger;
            Stopped = Task.Run(RunProducerAsync);
        }

        /// <summary>
        /// Completes once the tail has read the end of the log, or fails with the error that stopped it first.
        /// </summary>
        public Task Started => _startedTcs.Task;

        /// <summary>
        /// Completes when the session has stopped or failed. It never faults.
        /// </summary>
        public Task Stopped { get; }

        /// <summary>
        /// Whether the session has stopped or failed, which it records before it notifies anyone.
        /// </summary>
        public bool IsCompleted => Volatile.Read(ref _state).Completion is not null;

        /// <summary>
        /// The number of attachments. The tail's gate guards it.
        /// </summary>
        public int RefCount { get; set; }

        public IDisposable Attach(ITailObserver<TSourceEvent> observer, string observerId)
        {
            var attachment = new SessionAttachment(observer, observerId, Detach);

            TryUpdate(
                static (current, attachment) => current.Completion switch
                {
                    { Error: { } error } =>
                        throw new InvalidOperationException("Cannot attach to a failed event log tail.", error),

                    { Error: null } =>
                        throw new InvalidOperationException("Cannot attach to a stopped event log tail."),

                    _ => new SessionState(current.Attachments.Add(attachment))
                },
                attachment,
                out var next);

            _logger?.TailObserverAttached(_tailId, observerId, next.Attachments.Length);
            return attachment;
        }

        public async Task StopAsync()
        {
            if (Interlocked.Exchange(ref _stopping, 1) != 0)
            {
                await Stopped.ConfigureAwait(false);
                return;
            }

            _logger?.TailStopping(_tailId);

            using (_stopCts)
            {
                await _stopCts.CancelAsync().ConfigureAwait(false);
                await Stopped.ConfigureAwait(false);
            }
        }

        private async Task RunProducerAsync()
        {
            _logger?.TailStarting(_tailId);

            var stopToken = _stopCts.Token;

            try
            {
                var cursor = new EventLogCursor<TEvent, TSourceEvent, TStreamId, TStreamPos, TLogPos>(
                    _tail._sourceFactory(),
                    _tail._options,
                    _logger,
                    _tailId);

                await using (cursor.ConfigureAwait(false))
                {
                    await cursor.SeekToEndAsync(stopToken).ConfigureAwait(false);

                    _logger?.TailStarted(_tailId);
                    _startedTcs.TrySetResult();

                    // The cursor retries transient errors itself, so whatever it throws stops the tail.
                    while (true)
                        await cursor.ReadAsync(NotifyNext, stopToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
            {
                Complete(null);
                _startedTcs.TrySetCanceled(stopToken);
            }
            catch (Exception ex)
            {
                _logger?.TailFailed(ex, _tailId);
                Complete(ex);
                _startedTcs.TrySetException(ex);
            }
            finally
            {
                _logger?.TailStopped(_tailId);
                CorrelationId.Release(_tailId);
            }
        }

        private void NotifyNext(TSourceEvent sourceEvent)
        {
            foreach (var attachment in Volatile.Read(ref _state).Attachments)
            {
                try
                {
                    attachment.Observer.OnNext(sourceEvent);
                }
                catch (Exception observerException)
                {
                    _logger?.TailObserverOnNextFailed(observerException, _tailId, attachment.ObserverId);
                    attachment.Dispose();
                }
            }
        }

        private void Complete(Exception? error)
        {
            // Completing first refuses new attachments, so no observer can join after the error is handed out.
            if (!TryUpdate(
                    static (current, completion) => current.Completion is null
                        ? new SessionState(current.Attachments, completion)
                        : null,
                    new SessionCompletion(error),
                    out var completed) ||
                error is null)
            {
                return;
            }

            foreach (var attachment in completed.Attachments)
            {
                try
                {
                    attachment.Observer.OnError(error);
                }
                catch (Exception observerException)
                {
                    _logger?.TailObserverOnErrorFailed(observerException, _tailId, attachment.ObserverId);
                }
            }
        }

        private void Detach(SessionAttachment attachment)
        {
            TryUpdate(
                static (current, attachment) =>
                    new SessionState(current.Attachments.Remove(attachment), current.Completion),
                attachment,
                out var next);

            _logger?.TailObserverDetached(_tailId, attachment.ObserverId, next.Attachments.Length);
        }

        private bool TryUpdate<TArg>(
            Func<SessionState, TArg, SessionState?> updater,
            TArg argument,
            out SessionState result)
        {
            while (true)
            {
                var current = Volatile.Read(ref _state);
                var next = updater(current, argument);

                if (next is null)
                {
                    result = current;
                    return false;
                }

                if (!ReferenceEquals(Interlocked.CompareExchange(ref _state, next, current), current))
                    continue;

                result = next;
                return true;
            }
        }
    }

    private sealed class SessionState(
        ImmutableArray<SessionAttachment> attachments,
        SessionCompletion? completion = null)
    {
        public ImmutableArray<SessionAttachment> Attachments { get; } = attachments;
        public SessionCompletion? Completion { get; } = completion;
    }

    private sealed class SessionAttachment(
        ITailObserver<TSourceEvent> observer,
        string observerId,
        Action<SessionAttachment> onDispose) :
        IDisposable
    {
        private int _disposed;

        public ITailObserver<TSourceEvent> Observer { get; } = observer;
        public string ObserverId { get; } = observerId;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                onDispose(this);
        }
    }

    private sealed class SessionCompletion(Exception? error = null)
    {
        public Exception? Error { get; } = error;
    }

    private sealed class Attachment(Func<Task> onDispose) : IAsyncDisposable
    {
        private int _disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                await onDispose().ConfigureAwait(false);
        }
    }
}