using DomainBlocks.EventStore.Internal.Feeds;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace DomainBlocks.EventStore.PostgreSQL.Feeds;

/// <summary>
/// Validates options for an <see cref="EventLogFeed{T}"/>.
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
/// Pumps items from an <see cref="IEventLogSession{T}"/> to observers, reconnecting with backoff when the session is
/// lost.
/// </summary>
/// <remarks>
/// A session that doesn't resume the previous one sees only rows committed after it is established, so observers get
/// <see cref="IFeedObserver{T}.OnResetAsync"/> first and recover missed rows by re-reading from their last
/// position.
/// </remarks>
internal sealed class EventLogFeed<T> : IFeed<T, long>
{
    private readonly IEventLogSessionFactory<T> _sessionFactory;
    private readonly Func<CancellationToken, Task<IEventLogSession<T>>> _openSession;
    private readonly ILogger? _logger;
    private readonly FeedObserverHub<T> _observerHub;
    private readonly string _feedId = CorrelationId.ReserveGenerated();
    private readonly TaskCompletionSource _connectedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _connected;

    /// <remarks>
    /// The feed owns <paramref name="sessionFactory"/>, and disposes it once it has stopped.
    /// </remarks>
    public EventLogFeed(
        IEventLogSessionFactory<T> sessionFactory,
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

    public async Task<IFeedConnection<long>> ConnectAsync(CancellationToken cancellationToken = default)
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
        throw new InvalidOperationException("The event log feed completed before it connected.");
    }

    private static Func<CancellationToken, Task<IEventLogSession<T>>> AddResilience(
        IEventLogSessionFactory<T> sessionFactory,
        EventLogFeedOptions options,
        ILogger? logger,
        string feedId)
    {
        // Polly requires at least one retry.
        if (options.MaxRetryAttempts == 0)
            return sessionFactory.OpenAsync;

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
                    var shouldHandle = args.Outcome.Exception is { } ex &&
                                       ex is not OperationCanceledException &&
                                       sessionFactory.IsTransient(ex);

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
            .ExecuteAsync(async innerCt => await sessionFactory.OpenAsync(innerCt).ConfigureAwait(false), ct)
            .ConfigureAwait(false);
    }

    private sealed class Connection : IFeedConnection<long>
    {
        private readonly EventLogFeed<T> _feed;
        private readonly FeedObserverHub<T> _observerHub;
        private readonly ILogger? _logger;
        private readonly string _feedId;
        private readonly Task _producerTask;
        private readonly CancellationTokenSource _stopCts = new();
        private readonly TaskCompletionSource _completionTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private long _positionHighWaterMark = -1;
        private int _disposed;

        public Connection(EventLogFeed<T> feed)
        {
            _feed = feed;
            _observerHub = feed._observerHub;
            _logger = feed._logger;
            _feedId = feed._feedId;

            _producerTask = RunProducerAsync();
        }

        public Task Completion => _completionTcs.Task;

        /// <summary>
        /// Gets a position at or after every row that the feed has delivered, or -1 while there is nothing to cover.
        /// </summary>
        public long HighWaterMark => Volatile.Read(ref _positionHighWaterMark);

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

                        // A session can start with a mark, such as one that covers rows it won't deliver, and
                        // observers read the mark as soon as the feed connects.
                        RaiseHighWaterMark(session.HighWaterMark);

                        // Unless the session resumes the previous one, rows committed in between were missed. Reset
                        // observers before pumping the new session, so no row advances a position past the gap.
                        if (hasConnectedBefore && !session.IsResumed)
                            await _observerHub.NotifyResetAsync(_stopCts.Token).ConfigureAwait(false);

                        hasConnectedBefore = true;
                        _feed._connectedTcs.TrySetResult();

                        try
                        {
                            await foreach (var item in session.ReadAsync(_stopCts.Token).ConfigureAwait(false))
                            {
                                // The mark is raised before observers are notified, and the exchange is a full
                                // fence, so an observer that attaches too late for a row reads a mark that covers it.
                                RaiseHighWaterMark(session.HighWaterMark);

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

                try
                {
                    await _observerHub.NotifyErrorAsync(ex, _stopCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_stopCts.IsCancellationRequested)
                {
                    _logger?.FeedCanceled(_feedId);
                }

                _completionTcs.TrySetException(ex);
            }
            finally
            {
                await _feed._sessionFactory.DisposeAsync().ConfigureAwait(false);
                _logger?.FeedStopped(_feedId);
            }
        }

        // Only the producer writes the mark. A session reports -1 until its first row, so the mark only moves
        // forwards, keeping the rows that earlier sessions delivered.
        private void RaiseHighWaterMark(long position)
        {
            if (position > _positionHighWaterMark)
                Interlocked.Exchange(ref _positionHighWaterMark, position);
        }
    }
}