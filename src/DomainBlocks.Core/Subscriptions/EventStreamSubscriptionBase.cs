namespace DomainBlocks.Core.Subscriptions;

public abstract class EventStreamSubscriptionBase<TEvent, TPosition> : IEventStreamSubscription
    where TPosition : struct, IEquatable<TPosition>, IComparable<TPosition>
{
    private readonly IEventStreamSubscriber<TEvent, TPosition> _subscriber;
    private readonly SharedNotification _sharedNotification = new();
    private readonly SemaphoreSlim _emptyCount = new(1, 1);
    private readonly SemaphoreSlim _fullCount = new(0, 1);
    private readonly CheckpointTimer _checkpointTimer = new(Timeout.InfiniteTimeSpan);

    private readonly TaskCompletionSource _eventLoopCompletedSignal =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private CancellationTokenSource _cancellationTokenSource = null!;
    private IDisposable? _subscription;
    private TPosition? _lastPosition;
    private TPosition? _lastProcessedPosition;
    private int _checkpointEventCount;
    private CheckpointFrequency _checkpointFrequency = CheckpointFrequency.Default;

    protected EventStreamSubscriptionBase(IEventStreamSubscriber<TEvent, TPosition> subscriber)
    {
        _subscriber = subscriber;
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

        _lastPosition = await _subscriber.OnStarting(_cancellationTokenSource.Token);
        RunEventLoop();
        _subscription = await Subscribe(_lastPosition, _cancellationTokenSource.Token);
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
                var completedTask = await Task.WhenAny(nextNotification, _checkpointTimer.Task);

                if (completedTask == nextNotification)
                {
                    await HandleNotification(_sharedNotification);
                    _emptyCount.Release();
                    nextNotification = _fullCount.WaitAsync(_cancellationTokenSource.Token);
                }
                else
                {
                    await HandleCheckpointTimerElapsed();
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
            _checkpointTimer.Dispose();
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
        await _subscriber.OnCatchingUp(_cancellationTokenSource.Token);
        _checkpointFrequency = _subscriber.CatchUpCheckpointFrequency;
        _checkpointTimer.Reset(_checkpointFrequency.PerTimeInterval ?? Timeout.InfiniteTimeSpan);
    }

    private async Task HandleEvent(TEvent @event, TPosition position)
    {
        var isHandled = false;
        var result = OnEventResult.Ignored;

        do
        {
            try
            {
                result = await _subscriber.OnEvent(@event, position, _cancellationTokenSource.Token);
                isHandled = true;
                break;
            }
            catch (Exception ex)
            {
                var resolution = await _subscriber.OnEventError(@event, position, ex, _cancellationTokenSource.Token);

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

        _lastPosition = position;

        if (result == OnEventResult.Processed)
        {
            _lastProcessedPosition = position;
            _checkpointEventCount += _checkpointFrequency.CanCheckpoint ? 1 : 0;

            if (_checkpointEventCount == _checkpointFrequency.PerEventCount)
            {
                await _subscriber.OnCheckpoint(position, _cancellationTokenSource.Token);
                _checkpointEventCount = 0;
                _checkpointTimer.Reset();
            }
        }
    }

    private async Task HandleLive()
    {
        // Close off any remaining catch-up phase checkpoint if we processed any events.
        if (_checkpointEventCount > 0)
        {
            await _subscriber.OnCheckpoint(_lastProcessedPosition!.Value, _cancellationTokenSource.Token);
            _checkpointEventCount = 0;
        }

        await _subscriber.OnLive(_cancellationTokenSource.Token);
        _checkpointFrequency = _subscriber.LiveCheckpointFrequency;
        _checkpointTimer.Reset(_checkpointFrequency.PerTimeInterval ?? Timeout.InfiniteTimeSpan);
    }

    private Task HandleSubscriptionDropped(SubscriptionDroppedReason reason, Exception? exception)
    {
        return _subscriber.OnSubscriptionDropped(reason, exception, _cancellationTokenSource.Token);
    }

    private async Task HandleCheckpointTimerElapsed()
    {
        if (_checkpointEventCount > 0)
        {
            await _subscriber.OnCheckpoint(_lastProcessedPosition!.Value, _cancellationTokenSource.Token);
            _checkpointEventCount = 0;
        }

        _checkpointTimer.Reset();
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

    private sealed class CheckpointTimer : IDisposable
    {
        private TimeSpan _duration;
        private CancellationTokenSource _cancellationTokenSource = new();

        public CheckpointTimer(TimeSpan duration)
        {
            _duration = duration;
            Task = Task.Delay(duration, _cancellationTokenSource.Token);
        }

        public Task Task { get; private set; }

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

            Task = Task.Delay(_duration, _cancellationTokenSource.Token);
        }

        public void Dispose()
        {
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
        }
    }
}