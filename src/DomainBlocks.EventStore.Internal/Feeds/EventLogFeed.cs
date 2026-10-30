using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace DomainBlocks.EventStore.Internal.Feeds;

/// <summary>
/// Represents a feed that pumps items from a store's sessions to observers, creating a new session with backoff when
/// one is lost.
/// </summary>
/// <remarks>
/// <para>
/// A session that doesn't resume the previous one sees only items committed after it is created, so observers get
/// <see cref="IFeedObserver{T}.OnResetAsync"/> first and recover missed items by re-reading from their last position.
/// </para>
/// <para>
/// A store derives from this class to supply its sessions. If it holds anything across sessions, such as a replication
/// slot, it overrides <see cref="DisposeAsyncCore"/>, and calls the base method first, which stops the feed.
/// </para>
/// </remarks>
internal abstract class EventLogFeed<T, TMark> : IEventLogFeed<T, TMark>
    where TMark : IComparable<TMark>
{
    private readonly FeedResumePolicy _resumePolicy;
    private readonly FeedObserverHub<T> _observerHub;
    private readonly string _feedId = CorrelationId.ReserveGenerated();
    private readonly CancellationTokenSource _stopCts = new();
    private readonly TaskCompletionSource _connectedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _completionTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _producerTask;
    private TMark _highWaterMark = default!;
    private bool _hasHighWaterMark;
    private int _hasStarted;
    private int _isDisposed;

    // The feed reads the mark without a lock or an interlocked operation, so the mark must be a type whose reads and
    // writes are whole: a reference, or a primitive no wider than a pointer.
    static EventLogFeed()
    {
        var type = typeof(TMark);

        if (type is { IsValueType: true, IsPrimitive: false })
        {
            throw new NotSupportedException(
                $"A high-water mark must be a reference type or a primitive, but {type} is a struct, which can be " +
                "copied in parts.");
        }

        if (type.IsValueType && Unsafe.SizeOf<TMark>() > IntPtr.Size)
        {
            throw new NotSupportedException(
                $"A {type} high-water mark can be read while half written in a {IntPtr.Size * 8}-bit process, so " +
                "this store's live subscriptions require a 64-bit process.");
        }
    }

    protected EventLogFeed(FeedResumePolicy resumePolicy, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(resumePolicy);

        _resumePolicy = resumePolicy;
        _observerHub = new FeedObserverHub<T>(logger, _feedId);
        Logger = logger;
    }

    public Task Completion => _completionTcs.Task;

    // An observer reads the mark just after it attaches, and the attach ends with a compare-exchange on the observer
    // list, a full fence that orders the attach before this read. So the read needs no fence before it, only to be
    // whole, which the static constructor checks. The barrier keeps later reads after it.
    public TMark HighWaterMark
    {
        get
        {
            var highWaterMark = _highWaterMark;
            Volatile.ReadBarrier();
            return highWaterMark;
        }
    }

    protected ILogger? Logger { get; }

    public IDisposable Attach(IFeedObserver<T> observer, string correlationId = "unknown")
    {
        return _observerHub.Attach(observer, correlationId);
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

        if (Interlocked.Exchange(ref _hasStarted, 1) != 0)
            throw new InvalidOperationException("Connect may only be called once.");

        _producerTask = RunProducerAsync();

        Task task;

        try
        {
            task = await Task
                .WhenAny(_connectedTcs.Task, Completion)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The feed won't connect now, so it shouldn't keep trying until its owner disposes it.
            await StopAsync().ConfigureAwait(false);
            throw;
        }

        if (task == _connectedTcs.Task)
            return;

        await StopAsync().ConfigureAwait(false);
        await Completion.ConfigureAwait(false);
        throw new InvalidOperationException("The feed completed before it connected.");
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
            return;

        await DisposeAsyncCore().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Creates the next session, which continues where the previous one stopped if it can.
    /// </summary>
    /// <remarks>
    /// The feed creates a session only after it has disposed the previous one, so sessions never run at the same time.
    /// </remarks>
    protected internal abstract Task<IFeedSession<T, TMark>> CreateSessionAsync(
        CancellationToken cancellationToken);

    /// <summary>
    /// Stops the feed. Observers still attached receive an <see cref="ObjectDisposedException"/>.
    /// </summary>
    protected virtual async ValueTask DisposeAsyncCore()
    {
        await StopAsync().ConfigureAwait(false);

        _stopCts.Dispose();
        CorrelationId.Release(_feedId);
    }

    private async Task StopAsync()
    {
        // A feed that never started has no producer to complete it or to tell its observers.
        if (Interlocked.Exchange(ref _hasStarted, 1) == 0)
        {
            await _observerHub.NotifyDisposedAsync().ConfigureAwait(false);
            _completionTcs.TrySetResult();
            return;
        }

        if (!_stopCts.IsCancellationRequested)
        {
            Logger?.FeedStopping(_feedId);
            await _stopCts.CancelAsync().ConfigureAwait(false);
        }

        if (_producerTask is not null)
            await _producerTask.ConfigureAwait(false);
    }

    private async Task RunProducerAsync()
    {
        Logger?.FeedStarted(_feedId);

        try
        {
            var hasConnectedBefore = false;

            while (true)
            {
                _stopCts.Token.ThrowIfCancellationRequested();

                var session = await _resumePolicy
                    .ExecuteAsync(CreateSessionAsync, _feedId, _stopCts.Token)
                    .ConfigureAwait(false);

                await using (session.ConfigureAwait(false))
                {
                    Logger?.FeedConnected(_feedId, session.Description);

                    // A session can start with a mark, such as one that covers items it won't deliver, and observers
                    // read the mark as soon as the feed connects.
                    RaiseHighWaterMark(session.InitialMark);

                    // Unless the session resumes the previous one, items committed in between were missed. Reset
                    // observers before pumping the new session, so no item advances a position past the gap.
                    if (hasConnectedBefore && !session.IsResumed)
                        await _observerHub.NotifyResetAsync(_stopCts.Token).ConfigureAwait(false);

                    hasConnectedBefore = true;
                    _connectedTcs.TrySetResult();

                    try
                    {
                        await foreach (var (item, mark) in session.ReadAsync(_stopCts.Token).ConfigureAwait(false))
                        {
                            // The mark is raised before observers are notified, and the exchange is a full fence, so an
                            // observer that attaches too late for an item reads a mark that covers it.
                            RaiseHighWaterMark(mark);

                            await _observerHub.NotifyNextAsync(item, _stopCts.Token).ConfigureAwait(false);
                        }

                        // A live session should never end, so reconnect.
                        Logger?.FeedEnded(_feedId);
                    }
                    catch (Exception ex) when (!_stopCts.IsCancellationRequested && _resumePolicy.IsTransient(ex))
                    {
                        Logger?.FeedConnectionLost(ex, _feedId);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_stopCts.IsCancellationRequested)
        {
            Logger?.FeedCanceled(_feedId);
            await _observerHub.NotifyDisposedAsync().ConfigureAwait(false);
            _completionTcs.TrySetResult();
        }
        catch (Exception ex)
        {
            Logger?.FeedFailed(ex, _feedId);

            // An observer told of the error may attach again straight away. Completing first means it sees that this
            // feed has finished and gets a new one, rather than failing to attach to this one.
            _completionTcs.TrySetException(ex);

            try
            {
                await _observerHub.NotifyErrorAsync(ex, _stopCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_stopCts.IsCancellationRequested)
            {
                Logger?.FeedCanceled(_feedId);
            }
        }
        finally
        {
            Logger?.FeedStopped(_feedId);
        }
    }

    // Only the producer writes the mark, and it publishes the first session's mark as it is. After that, the mark only
    // moves forwards, so a session with nothing to cover yet leaves it as it is.
    private void RaiseHighWaterMark(TMark? mark)
    {
        if (mark is null || _hasHighWaterMark && mark.CompareTo(_highWaterMark) <= 0)
            return;

        Interlocked.Exchange(ref _highWaterMark, mark);
        _hasHighWaterMark = true;
    }
}