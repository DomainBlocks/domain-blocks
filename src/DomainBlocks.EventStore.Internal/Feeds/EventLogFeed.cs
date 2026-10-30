using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace DomainBlocks.EventStore.Internal.Feeds;

/// <summary>
/// Validates options for a <see cref="EventLogFeed{T, THighWaterMark}"/>.
/// </summary>
internal static class EventLogFeed
{
    // Polly rejects retry delays longer than 1 day.
    private static readonly TimeSpan PollyMaxDelay = TimeSpan.FromDays(1);

    /// <summary>
    /// Throws if a feed cannot use the options.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">An option is outside the range that a feed supports.</exception>
    public static void ValidateOptions(EventLogFeedOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.RetryDelay, PollyMaxDelay);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.MaxRetryDelay, PollyMaxDelay);
    }
}

/// <summary>
/// Represents a feed that pumps items from a store's sessions to observers, creating a new session with backoff when
/// one is lost.
/// </summary>
/// <remarks>
/// A session that doesn't resume the previous one sees only items committed after it is created, so observers get
/// <see cref="IFeedObserver{T}.OnResetAsync"/> first and recover missed items by re-reading from their last
/// position.
/// </remarks>
internal sealed class EventLogFeed<T, THighWaterMark> : IEventLogFeed<T, THighWaterMark>
    where THighWaterMark : IComparable<THighWaterMark>
{
    private readonly IFeedSessionFactory<T, THighWaterMark> _sessionFactory;
    private readonly Func<CancellationToken, Task<IFeedSession<T, THighWaterMark>>> _openSession;
    private readonly ILogger? _logger;
    private readonly FeedObserverHub<T> _observerHub;
    private readonly string _feedId = CorrelationId.ReserveGenerated();
    private readonly TaskCompletionSource _connectedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _connected;

    // The connection reads the mark without a lock or an interlocked operation, so the mark must be a type whose reads
    // and writes are whole: a reference, or a primitive no wider than a pointer.
    static EventLogFeed()
    {
        var type = typeof(THighWaterMark);

        if (type is { IsValueType: true, IsPrimitive: false })
        {
            throw new NotSupportedException(
                $"A high-water mark must be a reference type or a primitive, but {type} is a struct, which can be " +
                "copied in parts.");
        }

        if (type.IsValueType && Unsafe.SizeOf<THighWaterMark>() > IntPtr.Size)
        {
            throw new NotSupportedException(
                $"A {type} high-water mark can be read while half written in a {IntPtr.Size * 8}-bit process. Live " +
                "subscriptions require a 64-bit process.");
        }
    }

    /// <remarks>
    /// The feed owns <paramref name="sessionFactory"/>, and disposes it once it has stopped.
    /// </remarks>
    public EventLogFeed(
        IFeedSessionFactory<T, THighWaterMark> sessionFactory,
        EventLogFeedOptions? options = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(sessionFactory);

        options ??= EventLogFeedOptions.Default;

        _sessionFactory = sessionFactory;
        _openSession = AddResilience(sessionFactory, options, logger, _feedId);
        _logger = logger;
        _observerHub = new FeedObserverHub<T>(logger, _feedId);
    }

    public IDisposable Attach(IFeedObserver<T> observer, string correlationId = "unknown")
    {
        return _observerHub.Attach(observer, correlationId);
    }

    public async Task<IFeedConnection<THighWaterMark>> ConnectAsync(CancellationToken cancellationToken = default)
    {
        var connection = Interlocked.Exchange(ref _connected, 1) == 0
            ? new Connection(this)
            : throw new InvalidOperationException("Connect may only be called once.");

        Task task;

        try
        {
            task = await Task
                .WhenAny(_connectedTcs.Task, connection.Completion)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Nobody will own this connection, so stop the producer rather than leak it.
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        if (task == _connectedTcs.Task)
            return connection;

        await connection.DisposeAsync().ConfigureAwait(false);
        await connection.Completion.ConfigureAwait(false);
        throw new InvalidOperationException("The feed completed before it connected.");
    }

    private static Func<CancellationToken, Task<IFeedSession<T, THighWaterMark>>> AddResilience(
        IFeedSessionFactory<T, THighWaterMark> sessionFactory,
        EventLogFeedOptions options,
        ILogger? logger,
        string feedId)
    {
        // Polly requires at least one retry.
        if (options.MaxRetryAttempts == 0)
            return sessionFactory.CreateAsync;

        var resiliencePipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = options.MaxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = options.RetryDelay,
                MaxDelay = options.MaxRetryDelay,
                ShouldHandle = args =>
                {
                    var shouldHandle = args.Outcome.Exception is { } ex && sessionFactory.IsTransient(ex);

                    return ValueTask.FromResult(shouldHandle);
                },
                OnRetry = args =>
                {
                    if (args.Outcome.Exception is { } ex)
                        logger?.FeedRetrying(ex, feedId, args.AttemptNumber + 1, args.RetryDelay);

                    return ValueTask.CompletedTask;
                }
            })
            .Build();

        return async ct => await resiliencePipeline
            .ExecuteAsync(async innerCt => await sessionFactory.CreateAsync(innerCt).ConfigureAwait(false), ct)
            .ConfigureAwait(false);
    }

    private sealed class Connection : IFeedConnection<THighWaterMark>
    {
        private readonly EventLogFeed<T, THighWaterMark> _feed;
        private readonly FeedObserverHub<T> _observerHub;
        private readonly ILogger? _logger;
        private readonly string _feedId;
        private readonly Task _producerTask;
        private readonly CancellationTokenSource _stopCts = new();
        private readonly TaskCompletionSource _completionTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private THighWaterMark _highWaterMark = default!;
        private bool _hasHighWaterMark;
        private int _disposed;

        public Connection(EventLogFeed<T, THighWaterMark> feed)
        {
            _feed = feed;
            _observerHub = feed._observerHub;
            _logger = feed._logger;
            _feedId = feed._feedId;

            _producerTask = RunProducerAsync();
        }

        public Task Completion => _completionTcs.Task;

        // An observer reads the mark just after it attaches, and the attach ends with a compare-exchange on the
        // observer list, a full fence that orders the attach before this read. So the read needs no fence before it,
        // only to be whole, which the static constructor checks. The barrier keeps later reads after it.
        public THighWaterMark HighWaterMark
        {
            get
            {
                var highWaterMark = _highWaterMark;
                Volatile.ReadBarrier();
                return highWaterMark;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _logger?.FeedStopping(_feedId);

            using (_stopCts)
            {
                if (!_stopCts.IsCancellationRequested)
                    await _stopCts.CancelAsync().ConfigureAwait(false);

                await _producerTask.ConfigureAwait(false);
            }

            CorrelationId.Release(_feedId);
        }

        private async Task RunProducerAsync()
        {
            _logger?.FeedStarted(_feedId);

            try
            {
                var hasConnectedBefore = false;

                while (true)
                {
                    _stopCts.Token.ThrowIfCancellationRequested();

                    var session = await _feed._openSession(_stopCts.Token).ConfigureAwait(false);

                    await using (session.ConfigureAwait(false))
                    {
                        _logger?.FeedConnected(_feedId, session.Description);

                        // A session can start with a mark, such as one that covers items it won't deliver, and
                        // observers read the mark as soon as the feed connects.
                        RaiseHighWaterMark(session.InitialHighWaterMark);

                        // Unless the session resumes the previous one, items committed in between were missed. Reset
                        // observers before pumping the new session, so no item advances a position past the gap.
                        if (hasConnectedBefore && !session.IsResumed)
                            await _observerHub.NotifyResetAsync(_stopCts.Token).ConfigureAwait(false);

                        hasConnectedBefore = true;
                        _feed._connectedTcs.TrySetResult();

                        try
                        {
                            await foreach (var (item, highWaterMark) in session
                                               .ReadAsync(_stopCts.Token)
                                               .ConfigureAwait(false))
                            {
                                // The mark is raised before observers are notified, and the exchange is a full
                                // fence, so an observer that attaches too late for an item reads a mark that covers it.
                                RaiseHighWaterMark(highWaterMark);

                                await _observerHub.NotifyNextAsync(item, _stopCts.Token).ConfigureAwait(false);
                            }

                            // A live session should never end, so reconnect.
                            _logger?.FeedEnded(_feedId);
                        }
                        catch (Exception ex) when (!_stopCts.IsCancellationRequested &&
                                                   _feed._sessionFactory.IsTransient(ex))
                        {
                            _logger?.FeedConnectionLost(ex, _feedId);
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (_stopCts.IsCancellationRequested)
            {
                _logger?.FeedCanceled(_feedId);
                await _observerHub.NotifyDisposedAsync().ConfigureAwait(false);
                _completionTcs.TrySetResult();
            }
            catch (Exception ex)
            {
                _logger?.FeedFailed(ex, _feedId);

                // An observer told of the error may attach again straight away. Completing first means it sees that
                // this connection has finished and gets a new one, rather than failing to attach to this one.
                _completionTcs.TrySetException(ex);

                try
                {
                    await _observerHub.NotifyErrorAsync(ex, _stopCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_stopCts.IsCancellationRequested)
                {
                    _logger?.FeedCanceled(_feedId);
                }
            }
            finally
            {
                await _feed._sessionFactory.DisposeAsync().ConfigureAwait(false);
                _logger?.FeedStopped(_feedId);
            }
        }

        // Only the producer writes the mark, and it publishes the first session's mark as it is. After that, the mark
        // only moves forwards, so a session with nothing to cover yet leaves it as it is.
        private void RaiseHighWaterMark(THighWaterMark? mark)
        {
            if (mark is null || _hasHighWaterMark && mark.CompareTo(_highWaterMark) <= 0)
                return;

            Interlocked.Exchange(ref _highWaterMark, mark);
            _hasHighWaterMark = true;
        }
    }
}