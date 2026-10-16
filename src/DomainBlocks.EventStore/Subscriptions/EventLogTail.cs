using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace DomainBlocks.EventStore.Subscriptions;

/// <summary>
/// Reads a store's event log in order and hands each event to every attached observer. The tail starts on the first
/// attach and stops on the last detach. A tail that has failed is replaced on the next attach.
/// </summary>
/// <remarks>
/// <para>
/// A tail starts at the end of the log, then reads the events after the last one it read, up to
/// <see cref="EventLogTailOptions.MaxBatchSize"/> at a time. After a full batch it reads again at once. Otherwise it
/// waits until the source signals an append, or for <see cref="EventLogTailOptions.PollInterval"/>.
/// </para>
/// <para>
/// A transient error is retried from the same position with backoff, so observers never miss an event. Any other error
/// stops the tail and is passed to every observer.
/// </para>
/// <para>
/// An async gate serializes the tail's lifecycle: starting a run, counting its attachments, and stopping it, so the next
/// run starts only after the previous one has stopped. The read loop never takes the gate. A run's observers are held in
/// an immutable array that is replaced by compare-and-swap, never changed in place, so the read loop never blocks, even
/// when it detaches an observer that threw or records a failure.
/// </para>
/// </remarks>
internal sealed class EventLogTail<TSourceEvent> : IAsyncDisposable
{
    private readonly Func<IEventLogSource<TSourceEvent>> _sourceFactory;
    private readonly EventLogTailOptions _options;
    private readonly ILogger? _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _disposeCts = new();
    private Run? _current;
    private bool _disposed;

    public EventLogTail(
        Func<IEventLogSource<TSourceEvent>> sourceFactory,
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
        catch (OperationCanceledException) when (
            _disposeCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new ObjectDisposedException(GetType().FullName);
        }
    }

    /// <summary>
    /// Stops the tail and rejects further attachments. Observers that are still attached are not notified.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await _disposeCts.CancelAsync().ConfigureAwait(false);
        await _gate.WaitAsync().ConfigureAwait(false);

        try
        {
            if (_disposed)
                return;

            _disposed = true;

            if (_current is { } run)
            {
                _current = null;
                await run.StopAsync().ConfigureAwait(false);
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
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_current is null || _current.IsCompleted)
            {
                // A run that has failed may still be finishing. It has already disposed of its source, so waiting for it
                // only keeps its last log lines ahead of the next run's.
                if (_current is not null)
                    await _current.Stopped.ConfigureAwait(false);

                _current = new Run(this);
            }

            var run = _current;

            try
            {
                await run.Started.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested && run.RefCount == 0)
            {
                // Nobody else is attached, so stop the tail rather than leave it running.
                _current = null;
                await run.StopAsync().ConfigureAwait(false);
                throw;
            }

            var registration = run.Attach(observer, correlationId);
            run.RefCount++;

            return new Attachment(() => DetachAsync(run, registration));
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task DetachAsync(Run run, IDisposable registration)
    {
        registration.Dispose();

        await _gate.WaitAsync().ConfigureAwait(false);

        try
        {
            if (--run.RefCount > 0)
                return;

            if (ReferenceEquals(_current, run))
                _current = null;

            // This runs within the gate, so the next run cannot start until this one has stopped, and a store never has
            // two tails reading its log at once.
            await run.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// One run of the tail, from its start to its stop or failure.
    /// </summary>
    private sealed class Run
    {
        private readonly EventLogTail<TSourceEvent> _tail;
        private readonly ILogger? _logger;
        private readonly string _tailId = CorrelationId.ReserveGenerated();
        private readonly CancellationTokenSource _stopCts = new();
        private readonly TaskCompletionSource _startedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private State _state = new([]);
        private int _stopping;

        public Run(EventLogTail<TSourceEvent> tail)
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
        /// Completes when the run has stopped or failed. It never faults.
        /// </summary>
        public Task Stopped { get; }

        /// <summary>
        /// Whether the run has stopped or failed, which it records before it notifies anyone.
        /// </summary>
        public bool IsCompleted => Volatile.Read(ref _state).Completion is not null;

        /// <summary>
        /// The number of attachments. The tail's gate guards it.
        /// </summary>
        public int RefCount { get; set; }

        public IDisposable Attach(ITailObserver<TSourceEvent> observer, string observerId)
        {
            var registration = new Registration(observer, observerId, Detach);

            TryUpdate(
                static (current, registration) => current.Completion switch
                {
                    { Error: { } error } => throw new InvalidOperationException(
                        "Cannot attach to a failed event log tail.",
                        error),

                    { Error: null } => throw new InvalidOperationException(
                        "Cannot attach to a stopped event log tail."),

                    _ => new State(current.Registrations.Add(registration))
                },
                registration,
                out var next);

            _logger?.TailObserverAttached(_tailId, observerId, next.Registrations.Length);
            return registration;
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

            var options = _tail._options;
            var stopToken = _stopCts.Token;

            try
            {
                var source = _tail._sourceFactory();

                await using (source.ConfigureAwait(false))
                {
                    var pipeline = CreatePipeline(source);
                    var signal = source.CreateAppendSignal();

                    await using (signal.ConfigureAwait(false))
                    {
                        await pipeline
                            .ExecuteAsync(static (source, ct) => source.StartAsync(ct), source, stopToken)
                            .ConfigureAwait(false);

                        _logger?.TailStarted(_tailId);
                        _startedTcs.TrySetResult();

                        while (true)
                        {
                            var batch = await pipeline
                                .ExecuteAsync(
                                    static (state, ct) => state.Source.ReadNextAsync(state.MaxCount, ct),
                                    (Source: source, MaxCount: options.MaxBatchSize),
                                    stopToken)
                                .ConfigureAwait(false);

                            for (var i = 0; i < batch.Count; i++)
                                NotifyNext(batch[i]);

                            // A full batch may have left events unread, so only a short one waits.
                            if (batch.Count < options.MaxBatchSize)
                                await signal.WaitAsync(options.PollInterval, stopToken).ConfigureAwait(false);
                        }
                    }
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

        private ResiliencePipeline CreatePipeline(IEventLogSource<TSourceEvent> source)
        {
            var options = _tail._options;

            return new ResiliencePipelineBuilder()
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = int.MaxValue,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = options.RetryDelay,
                    MaxDelay = options.MaxRetryDelay,
                    ShouldHandle = args =>
                    {
                        var shouldHandle = args.Outcome.Exception is { } ex &&
                                           !args.Context.CancellationToken.IsCancellationRequested &&
                                           source.IsTransient(ex);

                        return ValueTask.FromResult(shouldHandle);
                    },
                    OnRetry = args =>
                    {
                        if (args.Outcome.Exception is { } ex)
                            _logger?.TailRetrying(ex, _tailId, args.AttemptNumber + 1, args.RetryDelay);

                        return ValueTask.CompletedTask;
                    }
                })
                .Build();
        }

        private void NotifyNext(TSourceEvent sourceEvent)
        {
            foreach (var registration in Volatile.Read(ref _state).Registrations)
            {
                try
                {
                    registration.Observer.OnNext(sourceEvent);
                }
                catch (Exception observerException)
                {
                    _logger?.TailObserverOnNextFailed(observerException, _tailId, registration.ObserverId);
                    registration.Dispose();
                }
            }
        }

        private void Complete(Exception? error)
        {
            // Completing first refuses new attachments, so no observer can join after the error is handed out.
            if (!TryUpdate(
                    static (current, completion) => current.Completion is null
                        ? new State(current.Registrations, completion)
                        : null,
                    new Completion(error),
                    out var completed) ||
                error is null)
            {
                return;
            }

            foreach (var registration in completed.Registrations)
            {
                try
                {
                    registration.Observer.OnError(error);
                }
                catch (Exception observerException)
                {
                    _logger?.TailObserverOnErrorFailed(observerException, _tailId, registration.ObserverId);
                }
            }
        }

        private void Detach(Registration registration)
        {
            TryUpdate(
                static (current, registration) =>
                    new State(current.Registrations.Remove(registration), current.Completion),
                registration,
                out var next);

            _logger?.TailObserverDetached(_tailId, registration.ObserverId, next.Registrations.Length);
        }

        private bool TryUpdate<TArg>(Func<State, TArg, State?> updater, TArg argument, out State result)
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

        private sealed class State(ImmutableArray<Registration> registrations, Completion? completion = null)
        {
            public ImmutableArray<Registration> Registrations { get; } = registrations;
            public Completion? Completion { get; } = completion;
        }

        private sealed class Registration(
            ITailObserver<TSourceEvent> observer,
            string observerId,
            Action<Registration> onDispose) : IDisposable
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

        private sealed class Completion(Exception? error = null)
        {
            public Exception? Error { get; } = error;
        }
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