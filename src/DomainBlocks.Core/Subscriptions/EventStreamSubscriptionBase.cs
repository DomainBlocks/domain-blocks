namespace DomainBlocks.Core.Subscriptions;

public abstract class EventStreamSubscriptionBase<TEvent, TPosition> : IEventStreamSubscription
    where TPosition : struct, IEquatable<TPosition>, IComparable<TPosition>
{
    private readonly IEnumerable<IEventStreamSubscriber<TEvent, TPosition>> _subscribers;
    private readonly Dictionary<IEventStreamSubscriber<TEvent, TPosition>, SubscriberState> _subscriberStates = new();
    private readonly SharedNotification _sharedNotification = new();
    private readonly SemaphoreSlim _emptyCount = new(1, 1);
    private readonly SemaphoreSlim _fullCount = new(0, 1);

    private readonly TaskCompletionSource _eventLoopCompletedSignal =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private CancellationTokenSource _cancellationTokenSource = null!;
    private IDisposable? _subscription;

    protected EventStreamSubscriptionBase(IEnumerable<IEventStreamSubscriber<TEvent, TPosition>> subscribers)
    {
        _subscribers = subscribers;
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
        _subscriberStates.Clear();
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cancellationTokenSource.Token.Register(Dispose);

        // Get start positions of each subscriber.
        var tasks = _subscribers.Select(async x =>
        {
            var position = await x.OnStarting(cancellationToken);
            return (x, position);
        });

        var subscriberStartPositions = await Task.WhenAll(tasks);

        foreach (var (subscriber, startPosition) in subscriberStartPositions)
        {
            var subscriberState = new SubscriberState(subscriber, startPosition);
            _subscriberStates.Add(subscriber, subscriberState);
        }

        // Find the minimum start position to subscribe from.
        var minStartPosition = _subscriberStates.Values.Min(x => x.StartPosition);

        RunEventLoop();

        var subscribeTask = Subscribe(minStartPosition, _cancellationTokenSource.Token);

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

    protected abstract Task<IDisposable> Subscribe(
        TPosition? fromPositionExclusive,
        CancellationToken cancellationToken);

    protected Task NotifyCatchingUp() => NotifyAsync(x => x.SetCatchingUp(), _cancellationTokenSource.Token);

    protected Task NotifyEvent(TEvent @event, TPosition position, CancellationToken cancellationToken = default) =>
        NotifyAsync(x => x.SetEvent(@event, position), cancellationToken);

    protected Task NotifyLive() => NotifyAsync(x => x.SetLive(), _cancellationTokenSource.Token);

    protected void NotifySubscriptionDropped(SubscriptionDroppedReason reason, Exception? exception) =>
        Notify(x => x.SetSubscriptionDropped(reason, exception), _cancellationTokenSource.Token);

    private void RunEventLoop() => Task.Run(async () => await RunImpl(), _cancellationTokenSource.Token);

    private async Task RunImpl()
    {
        try
        {
            var nextNotification = _fullCount.WaitAsync(_cancellationTokenSource.Token);

            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                var checkpointTimerTasks = _subscriberStates.Values.Select(x => x.CheckpointTimer.Task);
                var completedTask = await Task.WhenAny(nextNotification, Task.WhenAny(checkpointTimerTasks));

                if (completedTask == nextNotification)
                {
                    await HandleNotification(_sharedNotification);
                    _emptyCount.Release();
                    nextNotification = _fullCount.WaitAsync(_cancellationTokenSource.Token);
                }
                else
                {
                    var checkpointTimerTask = (Task<IEventStreamSubscriber<TEvent, TPosition>>)completedTask;
                    var subscriber = await checkpointTimerTask;
                    var subscriberState = _subscriberStates[subscriber];
                    await HandleCheckpointTimerElapsed(subscriberState);
                }
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
            foreach (var state in _subscriberStates.Values)
            {
                state.CheckpointTimer.Dispose();
            }
        }

        if (_cancellationTokenSource.IsCancellationRequested)
        {
            _eventLoopCompletedSignal.SetCanceled(_cancellationTokenSource.Token);
            return;
        }

        _eventLoopCompletedSignal.SetResult();
    }

    private void Notify(Action<SharedNotification> notificationAction, CancellationToken cancellationToken)
    {
        _emptyCount.Wait(cancellationToken);
        notificationAction(_sharedNotification);
        _fullCount.Release();
    }

    private async Task NotifyAsync(Action<SharedNotification> notificationAction, CancellationToken cancellationToken)
    {
        cancellationToken = cancellationToken == default ? _cancellationTokenSource.Token : cancellationToken;
        await _emptyCount.WaitAsync(cancellationToken);
        notificationAction(_sharedNotification);
        _fullCount.Release();
    }

    private async Task HandleNotification(SharedNotification notification)
    {
        switch (notification.NotificationType)
        {
            case NotificationType.CatchingUp:
                await HandleCatchingUp();
                break;
            case NotificationType.Event:
                var @event = notification.Event!;
                var position = notification.Position!.Value;
                await HandleEvent(@event, position);
                break;
            case NotificationType.Live:
                await HandleLive();
                break;
            case NotificationType.SubscriptionDropped:
                var reason = _sharedNotification.SubscriptionDroppedReason!.Value;
                var exception = _sharedNotification.Exception;
                await HandleSubscriptionDropped(reason, exception);
                break;
        }
    }

    private async Task HandleCatchingUp()
    {
        var tasks = _subscriberStates.Keys.Select(x => x.OnCatchingUp(_cancellationTokenSource.Token));
        await Task.WhenAll(tasks);

        foreach (var state in _subscriberStates.Values)
        {
            state.CheckpointFrequency = state.Subscriber.CatchUpCheckpointFrequency;
            state.CheckpointTimer.Reset(state.CheckpointFrequency.PerTimeInterval ?? Timeout.InfiniteTimeSpan);
        }
    }

    private Task HandleEvent(TEvent @event, TPosition position)
    {
        var tasks = _subscriberStates.Values.Select(x => HandleEvent(@event, position, x));
        return Task.WhenAll(tasks);
    }

    private async Task HandleEvent(
        TEvent @event,
        TPosition position,
        SubscriberState subscriberState)
    {
        if (subscriberState.StartPosition != null && position.CompareTo(subscriberState.StartPosition.Value) < 1)
        {
            // This subscriber's start position is ahead of the event's position. Ignore.
            return;
        }

        var isHandled = false;
        var result = OnEventResult.Ignored;
        var subscriber = subscriberState.Subscriber;

        do
        {
            try
            {
                result = await subscriber.OnEvent(@event, position, _cancellationTokenSource.Token);
                isHandled = true;
                break;
            }
            catch (Exception ex)
            {
                var resolution = await subscriber.OnEventError(@event, position, ex, _cancellationTokenSource.Token);

                switch (resolution)
                {
                    case EventErrorResolution.Abort:
                        throw;
                    case EventErrorResolution.Retry:
                        break;
                    case EventErrorResolution.Skip:
                        isHandled = true;
                        break;
                    default:
                        throw new InvalidOperationException($"Unexpected EventErrorResolution value {resolution}");
                }
            }
        } while (!isHandled);

        subscriberState.LastPosition = position;

        if (result == OnEventResult.Processed)
        {
            subscriberState.LastProcessedPosition = position;
            subscriberState.CheckpointEventCount += subscriberState.CheckpointFrequency.CanCheckpoint ? 1 : 0;

            if (subscriberState.CheckpointEventCount == subscriberState.CheckpointFrequency.PerEventCount)
            {
                await subscriber.OnCheckpoint(position, _cancellationTokenSource.Token);
                subscriberState.CheckpointEventCount = 0;
                subscriberState.CheckpointTimer.Reset();
            }
        }
    }

    private async Task HandleLive()
    {
        var onCheckpointTasks = _subscriberStates.Values.Select(async state =>
        {
            if (state.CheckpointEventCount > 0)
            {
                await state.Subscriber.OnCheckpoint(state.LastProcessedPosition!.Value, _cancellationTokenSource.Token);
                state.CheckpointEventCount = 0;
            }
        });

        await Task.WhenAll(onCheckpointTasks);

        var onLiveTasks = _subscriberStates.Values.Select(async state =>
        {
            await state.Subscriber.OnLive(_cancellationTokenSource.Token);
            state.CheckpointFrequency = state.Subscriber.LiveCheckpointFrequency;
            state.CheckpointTimer.Reset(state.CheckpointFrequency.PerTimeInterval ?? Timeout.InfiniteTimeSpan);
        });

        await Task.WhenAll(onLiveTasks);
    }

    private Task HandleSubscriptionDropped(SubscriptionDroppedReason reason, Exception? exception)
    {
        var tasks = _subscriberStates.Values.Select(x =>
            x.Subscriber.OnSubscriptionDropped(reason, exception, _cancellationTokenSource.Token));

        return Task.WhenAll(tasks);
    }

    private async Task HandleCheckpointTimerElapsed(SubscriberState subscriberState)
    {
        if (subscriberState.CheckpointEventCount > 0)
        {
            await subscriberState.Subscriber.OnCheckpoint(
                subscriberState.LastProcessedPosition!.Value,
                _cancellationTokenSource.Token);

            subscriberState.CheckpointEventCount = 0;
        }

        subscriberState.CheckpointTimer.Reset();
    }

    private enum NotificationType
    {
        CatchingUp,
        Event,
        Live,
        SubscriptionDropped
    }

    private sealed class SharedNotification
    {
        public NotificationType NotificationType { get; private set; }
        public TEvent? Event { get; private set; }
        public TPosition? Position { get; private set; }
        public SubscriptionDroppedReason? SubscriptionDroppedReason { get; private set; }
        public Exception? Exception { get; private set; }

        public void SetCatchingUp()
        {
            Clear();
            NotificationType = NotificationType.CatchingUp;
        }

        public void SetEvent(TEvent @event, TPosition position)
        {
            Clear();
            NotificationType = NotificationType.Event;
            Event = @event;
            Position = position;
        }

        public void SetLive()
        {
            Clear();
            NotificationType = NotificationType.Live;
        }

        public void SetSubscriptionDropped(SubscriptionDroppedReason? reason, Exception? exception)
        {
            Clear();
            NotificationType = NotificationType.SubscriptionDropped;
            SubscriptionDroppedReason = reason;
            Exception = exception;
        }

        private void Clear()
        {
            Event = default;
            Position = null;
            SubscriptionDroppedReason = null;
            Exception = null;
        }
    }

    private sealed class SubscriberState
    {
        public SubscriberState(IEventStreamSubscriber<TEvent, TPosition> subscriber, TPosition? startPosition)
        {
            Subscriber = subscriber;
            StartPosition = startPosition;
            CheckpointTimer = new SubscriberCheckpointTimer(subscriber, Timeout.InfiniteTimeSpan);
            CheckpointFrequency = CheckpointFrequency.Default;
        }

        public IEventStreamSubscriber<TEvent, TPosition> Subscriber { get; }
        public TPosition? StartPosition { get; }
        public TPosition? LastPosition { get; set; }
        public TPosition? LastProcessedPosition { get; set; }
        public SubscriberCheckpointTimer CheckpointTimer { get; }
        public int CheckpointEventCount { get; set; }
        public CheckpointFrequency CheckpointFrequency { get; set; }
    }

    private sealed class SubscriberCheckpointTimer : IDisposable
    {
        private readonly IEventStreamSubscriber<TEvent, TPosition> _subscriber;
        private TimeSpan _duration;
        private CancellationTokenSource _cancellationTokenSource = new();

        public SubscriberCheckpointTimer(IEventStreamSubscriber<TEvent, TPosition> subscriber, TimeSpan duration)
        {
            _subscriber = subscriber;
            _duration = duration;

            Task = System.Threading.Tasks.Task
                .Delay(duration, _cancellationTokenSource.Token)
                .ContinueWith(_ => _subscriber);
        }

        public Task<IEventStreamSubscriber<TEvent, TPosition>> Task { get; private set; }

        public void Reset(TimeSpan duration)
        {
            _duration = duration;
            Reset();
        }

        public void Reset()
        {
            if (!Task.IsCompletedSuccessfully || !_cancellationTokenSource.TryReset())
            {
                _cancellationTokenSource.Cancel();
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = new CancellationTokenSource();
            }

            Task = System.Threading.Tasks.Task
                .Delay(_duration, _cancellationTokenSource.Token)
                .ContinueWith(_ => _subscriber);
        }

        public void Dispose()
        {
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
        }
    }
}