using DomainBlocks.Core.Subscriptions.Concurrency;

namespace DomainBlocks.Core.Subscriptions;

public partial class EventStreamSubscription<TEvent, TPosition> :
    IEventStreamSubscription,
    IEventStreamListener<TEvent, TPosition>
    where TPosition : struct, IEquatable<TPosition>, IComparable<TPosition>
{
    private const int DefaultNotificationQueueSize = 1;

    private readonly IEventStreamSubscriber<TEvent, TPosition> _subscriber;
    private readonly ConsumerSessionGroup _sessions;
    private readonly ArenaQueue<Notification> _queue = new(DefaultNotificationQueueSize);

    private readonly TaskCompletionSource _eventLoopCompletedSignal =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private CancellationTokenSource _cancellationTokenSource = null!;
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

        RunEventLoop();

        var subscribeTask = _subscriber.Subscribe(this, startPosition, _cancellationTokenSource.Token);

        // We wait for either the subscription or event loop to complete here, in case there is an error on the event
        // loop while catching up, causing it to terminate early.
        var task = await Task.WhenAny(subscribeTask, _eventLoopCompletedSignal.Task);

        if (task == _eventLoopCompletedSignal.Task)
        {
            await _eventLoopCompletedSignal.Task;
            return;
        }

        _subscription = await subscribeTask;
    }

    public Task WaitForCompletedAsync(CancellationToken cancellationToken = default) =>
        _eventLoopCompletedSignal.Task.WaitAsync(cancellationToken);

    private void RunEventLoop() => Task.Run(async () => await RunEventLoopImpl(), _cancellationTokenSource.Token);

    private async Task RunEventLoopImpl()
    {
        try
        {
            await foreach (var notification in _queue.ReadAllAsync(_cancellationTokenSource.Token))
            {
                await HandleNotification(notification);
            }
        }
        catch (OperationCanceledException ex)
        {
            _eventLoopCompletedSignal.SetCanceled(ex.CancellationToken);
            return;
        }
        catch (Exception ex)
        {
            _eventLoopCompletedSignal.SetException(ex);
            return;
        }
        finally
        {
            _sessions.Dispose();
        }

        if (_cancellationTokenSource.IsCancellationRequested)
        {
            _eventLoopCompletedSignal.SetCanceled(_cancellationTokenSource.Token);
            return;
        }

        _eventLoopCompletedSignal.SetResult();
    }

    private async Task HandleNotification(Notification notification)
    {
        switch (notification.NotificationType)
        {
            case NotificationType.CatchingUp:
                await _sessions.NotifyCatchingUp(_cancellationTokenSource.Token);
                break;
            case NotificationType.Event:
                var @event = notification.Event!;
                var position = notification.Position!.Value;
                await _sessions.NotifyEvent(@event, position, _cancellationTokenSource.Token);
                break;
            case NotificationType.Live:
                await _sessions.NotifyLive(_cancellationTokenSource.Token);
                break;
            case NotificationType.SubscriptionDropped:
                var reason = notification.SubscriptionDroppedReason!.Value;
                var exception = notification.Exception;
                await _sessions.NotifySubscriptionDropped(reason, exception, _cancellationTokenSource.Token);
                break;
            case NotificationType.CheckpointTimerElapsed:
                await _sessions.NotifyCheckpointTimerElapsed(notification.Consumer!, _cancellationTokenSource.Token);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(notification), $"Unknown notification type {notification.NotificationType}");
        }
    }

    // IEventStreamListener members
    Task IEventStreamListener<TEvent, TPosition>.OnCatchingUp(CancellationToken cancellationToken)
    {
        return _queue.WriteAsync(x => x.SetCatchingUp(), cancellationToken);
    }

    async Task<OnEventResult> IEventStreamListener<TEvent, TPosition>.OnEvent(
        TEvent @event, TPosition position, CancellationToken cancellationToken)
    {
        await _queue.WriteAsync(x => x.SetEvent(@event, position), cancellationToken);
        return OnEventResult.Processed;
    }

    Task IEventStreamListener<TEvent, TPosition>.OnLive(CancellationToken cancellationToken)
    {
        return _queue.WriteAsync(x => x.SetLive(), cancellationToken);
    }

    Task IEventStreamListener<TEvent, TPosition>.OnSubscriptionDropped(
        SubscriptionDroppedReason reason, Exception? exception, CancellationToken cancellationToken)
    {
        return _queue.WriteAsync(x => x.SetSubscriptionDropped(reason, exception), cancellationToken);
    }
}