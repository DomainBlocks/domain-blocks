using DomainBlocks.Core.Subscriptions.Concurrency;

namespace DomainBlocks.Core.Subscriptions;

public sealed class EventStreamSubscription<TEvent, TPosition> :
    IEventStreamSubscription,
    IEventStreamSubscriber<TEvent, TPosition>
    where TPosition : struct, IComparable<TPosition>
{
    private readonly ArenaQueue<QueueNotification<TEvent, TPosition>> _queue;
    private readonly IEventStream<TEvent, TPosition> _eventStream;
    private readonly IReadOnlyDictionary<Guid, EventStreamConsumerSession<TEvent, TPosition>> _sessions;
    private readonly IComparer<TPosition?> _positionComparer;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _eventLoopTask;
    private IDisposable? _subscription;

    public EventStreamSubscription(
        IEventStream<TEvent, TPosition> eventStream,
        IEnumerable<IEventStreamConsumer<TEvent, TPosition>> consumers,
        IComparer<TPosition?>? positionComparer = null,
        int queueSize = 1) :
        this(eventStream, consumers, new ArenaQueue<QueueNotification<TEvent, TPosition>>(queueSize), positionComparer)
    {
    }

    internal EventStreamSubscription(
        IEventStream<TEvent, TPosition> eventStream,
        IEnumerable<IEventStreamConsumer<TEvent, TPosition>> consumers,
        ArenaQueue<QueueNotification<TEvent, TPosition>> queue,
        IComparer<TPosition?>? positionComparer = null)
    {
        _eventStream = eventStream;

        _sessions = consumers
            .Select(x => new EventStreamConsumerSession<TEvent, TPosition>(x, queue))
            .ToDictionary(x => x.Id);

        _queue = queue;
        _positionComparer = positionComparer ?? Comparer<TPosition?>.Default;
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();

        foreach (var session in _sessions.Values)
        {
            session.Dispose();
        }

        _subscription?.Dispose();
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // The event loop needs to start first, as the call to NotifyStarting results in items being enqueued.
        // Starting it first ensures that queue writes won't block if the buffer is full.
        _eventLoopTask = RunEventLoop(_cancellationTokenSource.Token);
        var startPosition = await NotifyStarting(_cancellationTokenSource.Token);
        var subscribeTask = _eventStream.Subscribe(this, startPosition, _cancellationTokenSource.Token);

        var task = await Task.WhenAny(_eventLoopTask, subscribeTask);
        await task;
        _subscription = await subscribeTask;
    }

    public Task WaitForCompletedAsync(CancellationToken cancellationToken = default)
    {
        if (_eventLoopTask == null)
        {
            throw new InvalidOperationException("Subscription not started");
        }

        return _eventLoopTask.WaitAsync(cancellationToken);
    }

    private async Task<TPosition?> NotifyStarting(CancellationToken cancellationToken)
    {
        var tasks = _sessions.Values.Select(x => x.NotifyStarting(cancellationToken));
        var startPositions = await Task.WhenAll(tasks);
        return startPositions.Aggregate((acc, next) =>
        {
            var comparisonResult = _positionComparer.Compare(acc, next);
            return comparisonResult <= 0 ? acc : next;
        });
    }

    private Task RunEventLoop(CancellationToken cancellationToken)
    {
        return Task.Run(
            async () =>
            {
                await foreach (var notification in _queue.ReadAllAsync(cancellationToken))
                {
                    await HandleNotification(notification, cancellationToken);
                }
            },
            cancellationToken);
    }

    private async Task HandleNotification(
        QueueNotification<TEvent, TPosition> notification, CancellationToken cancellationToken)
    {
        switch (notification.NotificationType)
        {
            case QueueNotificationType.CatchingUp:
                await NotifyCatchingUp(cancellationToken);
                break;
            case QueueNotificationType.Event:
                var @event = notification.Event!;
                var position = notification.Position!.Value;
                await NotifyEvent(@event, position, cancellationToken);
                break;
            case QueueNotificationType.Live:
                await NotifyLive(cancellationToken);
                break;
            case QueueNotificationType.SubscriptionDropped:
                var reason = notification.SubscriptionDroppedReason!.Value;
                var exception = notification.Exception;
                await NotifySubscriptionDropped(reason, exception, cancellationToken);
                break;
            case QueueNotificationType.CheckpointTimerElapsed:
                var consumerSessionId = notification.ConsumerSessionId!.Value;
                await NotifyCheckpointTimerElapsed(consumerSessionId, cancellationToken);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(notification), $"Unknown notification type {notification.NotificationType}");
        }
    }

    private async Task NotifyCatchingUp(CancellationToken cancellationToken)
    {
        var tasks = _sessions.Values.Select(x => x.NotifyCatchingUp(cancellationToken));
        await Task.WhenAll(tasks);

        foreach (var session in _sessions.Values)
        {
            session.ResetCheckpointTimer();
        }
    }

    private Task NotifyEvent(TEvent @event, TPosition position, CancellationToken cancellationToken)
    {
        var tasks = _sessions.Values.Select(x => x.NotifyEvent(@event, position, cancellationToken));
        return Task.WhenAll(tasks);
    }

    private async Task NotifyLive(CancellationToken cancellationToken)
    {
        var notifyCheckpointTasks = _sessions.Values.Select(x => x.NotifyCheckpoint(cancellationToken));
        await Task.WhenAll(notifyCheckpointTasks);

        var notifyLiveTasks = _sessions.Values.Select(x => x.NotifyLive(cancellationToken));
        await Task.WhenAll(notifyLiveTasks);

        foreach (var session in _sessions.Values)
        {
            session.ResetCheckpointTimer();
        }
    }

    private Task NotifySubscriptionDropped(
        SubscriptionDroppedReason reason, Exception? exception, CancellationToken cancellationToken)
    {
        var tasks = _sessions.Values.Select(x => x.NotifySubscriptionDropped(reason, exception, cancellationToken));
        return Task.WhenAll(tasks);
    }

    private async Task NotifyCheckpointTimerElapsed(Guid sessionId, CancellationToken cancellationToken)
    {
        var session = _sessions[sessionId];
        await session.NotifyCheckpoint(cancellationToken);
        session.ResetCheckpointTimer();
    }

    // IEventStreamSubscriber members
    Task IEventStreamSubscriber<TEvent, TPosition>.OnCatchingUp(CancellationToken cancellationToken)
    {
        return _queue.WriteAsync(x => x.SetCatchingUp(), cancellationToken);
    }

    Task IEventStreamSubscriber<TEvent, TPosition>.OnEvent(
        TEvent @event, TPosition position, CancellationToken cancellationToken)
    {
        return _queue.WriteAsync(x => x.SetEvent(@event, position), cancellationToken);
    }

    Task IEventStreamSubscriber<TEvent, TPosition>.OnLive(CancellationToken cancellationToken)
    {
        return _queue.WriteAsync(x => x.SetLive(), cancellationToken);
    }

    Task IEventStreamSubscriber<TEvent, TPosition>.OnSubscriptionDropped(
        SubscriptionDroppedReason reason, Exception? exception, CancellationToken cancellationToken)
    {
        return _queue.WriteAsync(x => x.SetSubscriptionDropped(reason, exception), cancellationToken);
    }
}