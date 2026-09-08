using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace DomainBlocks.EventStore.PostgreSQL.Feeds;

/// <summary>
/// Pumps rows from an <see cref="IEventLogSession"/> to attached observers, re-establishing the session with backoff
/// when it is lost. Observers are told about every re-established session via
/// <see cref="IEventLogObserver.OnResetAsync"/>, before any row of the new session is delivered, because a new session
/// only sees rows committed after its own establishment point.
/// </summary>
/// <remarks>
/// Ported from the MongoDB change stream subject. Unlike a change stream there is no resume token: recovering the
/// rows missed during an outage is the observer's job, which it does by re-reading from its last position.
/// </remarks>
internal sealed class EventLogFeed : IEventLogFeed
{
    private readonly EventLogSessionFactory _sessionFactory;
    private readonly EventLogFeedOptions _options;
    private readonly ILogger? _logger;
    private readonly ConnectionState _connectionState;
    private readonly string _feedId = CorrelationId.ReserveGenerated();
    private readonly TaskCompletionSource _connectedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _connected;

    public EventLogFeed(
        EventLogSessionFactory sessionFactory,
        EventLogFeedOptions? options = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(sessionFactory);

        options ??= EventLogFeedOptions.Default;

        _sessionFactory = AddResilience(sessionFactory, options, logger, _feedId);
        _options = options;
        _logger = logger;
        _connectionState = new ConnectionState(logger, _feedId);
    }

    public IDisposable Attach(IEventLogObserver observer, string correlationId = "unknown")
    {
        return _connectionState.Attach(observer, correlationId);
    }

    public async Task<IEventLogFeedConnection> ConnectAsync(CancellationToken cancellationToken = default)
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
            // Nobody will ever own this connection, so stop the producer rather than leak it.
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        if (task == _connectedTcs.Task)
            return connection;

        await connection.Completion.ConfigureAwait(false);
        throw new InvalidOperationException("The event log feed completed before it connected.");
    }

    private static EventLogSessionFactory AddResilience(
        EventLogSessionFactory sessionFactory,
        EventLogFeedOptions options,
        ILogger? logger,
        string feedId)
    {
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
                                       options.IsTransient(ex);

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
            .ExecuteAsync(async innerCt => await sessionFactory(innerCt).ConfigureAwait(false), ct)
            .ConfigureAwait(false);
    }

    private sealed class Connection : IEventLogFeedConnection
    {
        private readonly EventLogFeed _feed;
        private readonly ConnectionState _state;
        private readonly ILogger? _logger;
        private readonly string _feedId;
        private readonly Task _producerTask;
        private readonly CancellationTokenSource _stopCts = new();
        private readonly TaskCompletionSource _completionTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposed;

        public Connection(EventLogFeed feed)
        {
            _feed = feed;
            _state = feed._connectionState;
            _logger = feed._logger;
            _feedId = feed._feedId;

            _producerTask = RunProducerAsync();
        }

        public Task Completion => _completionTcs.Task;

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

                    var session = await _feed._sessionFactory(_stopCts.Token).ConfigureAwait(false);

                    await using (session.ConfigureAwait(false))
                    {
                        _logger?.FeedConnected(_feedId, session.Description);

                        // Anything committed between the old session's loss and this session's establishment was
                        // missed. Tell observers before pumping any row of the new session, so that no such row can
                        // advance an observer's position past the gap.
                        if (hasConnectedBefore)
                            await _state.NotifyResetAsync(_stopCts.Token).ConfigureAwait(false);

                        hasConnectedBefore = true;
                        _feed._connectedTcs.TrySetResult();

                        try
                        {
                            await foreach (var row in session.ReadRowsAsync(_stopCts.Token).ConfigureAwait(false))
                                await _state.NotifyNextAsync(row, _stopCts.Token).ConfigureAwait(false);

                            // A live session is expected to remain open. Log a warning and reconnect.
                            _logger?.FeedEnded(_feedId);
                        }
                        catch (Exception ex) when (!_stopCts.IsCancellationRequested && _feed._options.IsTransient(ex))
                        {
                            _logger?.FeedConnectionLost(ex, _feedId);
                            // Continue outer loop (reconnect)
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (_stopCts.IsCancellationRequested)
            {
                _logger?.FeedCanceled(_feedId);
                _state.SetComplete();
                _completionTcs.TrySetResult();
            }
            catch (Exception ex)
            {
                _logger?.FeedFailed(ex, _feedId);

                try
                {
                    await _state.NotifyErrorAsync(ex, _stopCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_stopCts.IsCancellationRequested)
                {
                    _logger?.FeedCanceled(_feedId);
                }

                _completionTcs.TrySetException(ex);
            }
            finally
            {
                _logger?.FeedStopped(_feedId);
            }
        }
    }

    private sealed class ConnectionState(ILogger? logger, string feedId)
    {
        private State _state = new([]);

        public IDisposable Attach(IEventLogObserver observer, string observerId)
        {
            var attachment = new Attachment(observer, observerId, Detach);

            TryUpdate(
                static (current, attachment) => current.Completion switch
                {
                    { Error: { } error } => throw new InvalidOperationException(
                        "Cannot attach to a faulted event log feed.",
                        error),

                    { Error: null } => throw new InvalidOperationException(
                        "Cannot attach to a completed event log feed."),

                    _ => new State(current.Attachments.Add(attachment))
                },
                attachment,
                out var next);

            logger?.ObserverAttached(feedId, observerId, next.Attachments.Length);
            return attachment;
        }

        public async ValueTask NotifyNextAsync(EventLogRow row, CancellationToken cancellationToken)
        {
            foreach (var attachment in Volatile.Read(ref _state).Attachments)
            {
                try
                {
                    await attachment.Observer.OnNextAsync(row, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception observerException)
                {
                    logger?.ObserverOnNextFailed(observerException, feedId, attachment.ObserverId);
                    attachment.Dispose();
                }
            }
        }

        public async ValueTask NotifyResetAsync(CancellationToken cancellationToken)
        {
            var attachments = Volatile.Read(ref _state).Attachments;
            logger?.FeedReset(feedId, attachments.Length);

            foreach (var attachment in attachments)
            {
                try
                {
                    await attachment.Observer.OnResetAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception observerException)
                {
                    logger?.ObserverOnResetFailed(observerException, feedId, attachment.ObserverId);
                    attachment.Dispose();
                }
            }
        }

        public async ValueTask NotifyErrorAsync(Exception error, CancellationToken cancellationToken)
        {
            if (!TryUpdate(
                    static (current, error) => current.Completion is null
                        ? new State(current.Attachments, new Completion(error))
                        : null,
                    error,
                    out var faulted))
            {
                return;
            }

            foreach (var attachment in faulted.Attachments)
            {
                try
                {
                    await attachment.Observer.OnErrorAsync(error, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception observerException)
                {
                    logger?.ObserverOnErrorFailed(observerException, feedId, attachment.ObserverId);
                }
            }
        }

        public void SetComplete()
        {
            TryUpdate(
                static (current, completion) => current.Completion is null
                    ? new State(current.Attachments, completion)
                    : null,
                new Completion(),
                out _);
        }

        private void Detach(Attachment attachment)
        {
            TryUpdate(
                static (current, attachment) => new State(current.Attachments.Remove(attachment), current.Completion),
                attachment,
                out var next);

            logger?.ObserverDetached(feedId, attachment.ObserverId, next.Attachments.Length);
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

        private sealed class State(ImmutableArray<Attachment> attachments, Completion? completion = null)
        {
            public ImmutableArray<Attachment> Attachments { get; } = attachments;
            public Completion? Completion { get; } = completion;
        }

        private sealed class Attachment(IEventLogObserver observer, string observerId, Action<Attachment> onDispose) :
            IDisposable
        {
            private int _disposed;

            public IEventLogObserver Observer { get; } = observer;
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
}
