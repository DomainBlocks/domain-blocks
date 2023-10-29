using DomainBlocks.Core.Subscriptions.Concurrency;

namespace DomainBlocks.Core.Subscriptions;

public partial class EventStreamSubscription<TEvent, TPosition> : IEventStreamSubscription
    where TPosition : struct, IComparable<TPosition>
{
    private const int DefaultNotificationQueueSize = 1;

    private readonly IEventStreamSubscriber<TEvent, TPosition> _subscriber;
    private readonly ConsumerSessionGroup _sessions;
    private readonly ArenaQueue<Notification> _queue = new(DefaultNotificationQueueSize);

    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _eventLoopTask;
    private IDisposable? _subscription;

    public EventStreamSubscription(
        IEventStreamSubscriber<TEvent, TPosition> subscriber,
        IEnumerable<IEventStreamConsumer<TEvent, TPosition>> consumers)
    {
        _subscriber = subscriber;
        _sessions = new ConsumerSessionGroup(consumers, _queue);
    }

    public void Dispose()
    {
        if (_cancellationTokenSource is { IsCancellationRequested: false })
        {
            _cancellationTokenSource.Cancel();
        }

        // TODO: Potential race condition here?
        _subscription?.Dispose();
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cancellationTokenSource.Token.Register(Dispose);

        var startPosition = await _sessions.NotifyStarting(cancellationToken);

        _eventLoopTask = RunEventLoop();

        var subscribeTask = _subscriber.Subscribe(
            startPosition,
            ct => _queue.WriteAsync(x => x.SetCatchingUp(), ct),
            (e, pos, ct) => _queue.WriteAsync(x => x.SetEvent(e, pos), ct),
            ct => _queue.WriteAsync(x => x.SetLive(), ct),
            (reason, ex, ct) => _queue.WriteAsync(x => x.SetSubscriptionDropped(reason, ex), ct),
            _cancellationTokenSource.Token);

        // We wait for either the event loop or subscription to complete here, in case there is an error on the event
        // loop while catching up, causing it to terminate early.
        var task = await Task.WhenAny(_eventLoopTask, subscribeTask);
        await task;
        _subscription = await subscribeTask;

        return;

        async Task RunEventLoop()
        {
            await foreach (var notification in _queue.ReadAllAsync(_cancellationTokenSource.Token))
            {
                await HandleNotification(notification, _cancellationTokenSource.Token);
            }
        }
    }

    public Task WaitForCompletedAsync(CancellationToken cancellationToken = default)
    {
        if (_eventLoopTask == null)
        {
            throw new InvalidOperationException("Subscription not started");
        }

        return _eventLoopTask.WaitAsync(cancellationToken);
    }

    private async Task HandleNotification(Notification notification, CancellationToken cancellationToken)
    {
        switch (notification.NotificationType)
        {
            case NotificationType.CatchingUp:
                await _sessions.NotifyCatchingUp(cancellationToken);
                break;
            case NotificationType.Event:
                var @event = notification.Event!;
                var position = notification.Position!.Value;
                await _sessions.NotifyEvent(@event, position, cancellationToken);
                break;
            case NotificationType.Live:
                await _sessions.NotifyLive(cancellationToken);
                break;
            case NotificationType.SubscriptionDropped:
                var reason = notification.SubscriptionDroppedReason!.Value;
                var exception = notification.Exception;
                await _sessions.NotifySubscriptionDropped(reason, exception, cancellationToken);
                break;
            case NotificationType.CheckpointTimerElapsed:
                var consumerSessionId = notification.ConsumerSessionId!.Value;
                await _sessions.NotifyCheckpointTimerElapsed(consumerSessionId, cancellationToken);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(notification), $"Unknown notification type {notification.NotificationType}");
        }
    }
}