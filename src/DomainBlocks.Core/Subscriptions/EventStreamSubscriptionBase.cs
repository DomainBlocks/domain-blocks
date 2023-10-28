namespace DomainBlocks.Core.Subscriptions;

public abstract class EventStreamSubscriptionBase<TEvent, TPosition> : IEventStreamSubscription
    where TPosition : struct, IEquatable<TPosition>, IComparable<TPosition>
{
    private readonly IEnumerable<IEventStreamConsumer<TEvent, TPosition>> _consumers;
    private readonly Dictionary<IEventStreamConsumer<TEvent, TPosition>, ConsumerState> _consumerStates = new();
    private readonly SharedNotification _sharedNotification = new();
    private readonly SemaphoreSlim _emptyCount = new(1, 1);
    private readonly SemaphoreSlim _fullCount = new(0, 1);

    private readonly TaskCompletionSource _eventLoopCompletedSignal =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private CancellationTokenSource _cancellationTokenSource = null!;
    private IDisposable? _subscription;

    protected EventStreamSubscriptionBase(IEnumerable<IEventStreamConsumer<TEvent, TPosition>> consumers)
    {
        _consumers = consumers;
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
        _consumerStates.Clear();
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cancellationTokenSource.Token.Register(Dispose);

        // Get start positions of each consumer.
        var tasks = _consumers.Select(async x =>
        {
            var position = await x.OnStarting(cancellationToken);
            return (x, position);
        });

        var subscriberStartPositions = await Task.WhenAll(tasks);

        foreach (var (consumer, startPosition) in subscriberStartPositions)
        {
            var consumerState = new ConsumerState(consumer, startPosition);
            _consumerStates.Add(consumer, consumerState);
        }

        // Find the minimum start position to subscribe from.
        var minStartPosition = _consumerStates.Values.Min(x => x.StartPosition);

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
                var checkpointTimerTasks = _consumerStates.Values.Select(x => x.CheckpointTimer.Task);
                var completedTask = await Task.WhenAny(nextNotification, Task.WhenAny(checkpointTimerTasks));

                if (completedTask == nextNotification)
                {
                    await HandleNotification(_sharedNotification);
                    _emptyCount.Release();
                    nextNotification = _fullCount.WaitAsync(_cancellationTokenSource.Token);
                }
                else
                {
                    var checkpointTimerTask = (Task<IEventStreamConsumer<TEvent, TPosition>>)completedTask;
                    var consumer = await checkpointTimerTask;
                    var consumerState = _consumerStates[consumer];
                    await HandleCheckpointTimerElapsed(consumerState);
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
            foreach (var state in _consumerStates.Values)
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
        var tasks = _consumerStates.Keys.Select(x => x.OnCatchingUp(_cancellationTokenSource.Token));
        await Task.WhenAll(tasks);

        foreach (var state in _consumerStates.Values)
        {
            state.CheckpointFrequency = state.Consumer.CatchUpCheckpointFrequency;
            state.CheckpointTimer.Reset(state.CheckpointFrequency.PerTimeInterval ?? Timeout.InfiniteTimeSpan);
        }
    }

    private Task HandleEvent(TEvent @event, TPosition position)
    {
        var tasks = _consumerStates.Values.Select(x => HandleEvent(@event, position, x));
        return Task.WhenAll(tasks);
    }

    private async Task HandleEvent(
        TEvent @event,
        TPosition position,
        ConsumerState consumerState)
    {
        if (consumerState.StartPosition != null && position.CompareTo(consumerState.StartPosition.Value) < 1)
        {
            // This consumer's start position is ahead of the event's position. Ignore.
            return;
        }

        var isHandled = false;
        var result = OnEventResult.Ignored;
        var consumer = consumerState.Consumer;

        do
        {
            try
            {
                result = await consumer.OnEvent(@event, position, _cancellationTokenSource.Token);
                isHandled = true;
                break;
            }
            catch (Exception ex)
            {
                var resolution = await consumer.OnEventError(@event, position, ex, _cancellationTokenSource.Token);

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

        if (result == OnEventResult.Processed)
        {
            consumerState.LastProcessedPosition = position;
            consumerState.CheckpointEventCount += consumerState.CheckpointFrequency.CanCheckpoint ? 1 : 0;

            if (consumerState.CheckpointEventCount == consumerState.CheckpointFrequency.PerEventCount)
            {
                await consumer.OnCheckpoint(position, _cancellationTokenSource.Token);
                consumerState.CheckpointEventCount = 0;
                consumerState.CheckpointTimer.Reset();
            }
        }
    }

    private async Task HandleLive()
    {
        var onCheckpointTasks = _consumerStates.Values.Select(async state =>
        {
            if (state.CheckpointEventCount > 0)
            {
                await state.Consumer.OnCheckpoint(state.LastProcessedPosition!.Value, _cancellationTokenSource.Token);
                state.CheckpointEventCount = 0;
            }
        });

        await Task.WhenAll(onCheckpointTasks);

        var onLiveTasks = _consumerStates.Values.Select(async state =>
        {
            await state.Consumer.OnLive(_cancellationTokenSource.Token);
            state.CheckpointFrequency = state.Consumer.LiveCheckpointFrequency;
            state.CheckpointTimer.Reset(state.CheckpointFrequency.PerTimeInterval ?? Timeout.InfiniteTimeSpan);
        });

        await Task.WhenAll(onLiveTasks);
    }

    private Task HandleSubscriptionDropped(SubscriptionDroppedReason reason, Exception? exception)
    {
        var tasks = _consumerStates.Values.Select(x =>
            x.Consumer.OnSubscriptionDropped(reason, exception, _cancellationTokenSource.Token));

        return Task.WhenAll(tasks);
    }

    private async Task HandleCheckpointTimerElapsed(ConsumerState consumerState)
    {
        if (consumerState.CheckpointEventCount > 0)
        {
            await consumerState.Consumer.OnCheckpoint(
                consumerState.LastProcessedPosition!.Value,
                _cancellationTokenSource.Token);

            consumerState.CheckpointEventCount = 0;
        }

        consumerState.CheckpointTimer.Reset();
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

    private sealed class ConsumerState
    {
        public ConsumerState(IEventStreamConsumer<TEvent, TPosition> consumer, TPosition? startPosition)
        {
            Consumer = consumer;
            StartPosition = startPosition;
            CheckpointTimer = new CheckpointTimer(consumer, Timeout.InfiniteTimeSpan);
            CheckpointFrequency = CheckpointFrequency.Default;
        }

        public IEventStreamConsumer<TEvent, TPosition> Consumer { get; }
        public TPosition? StartPosition { get; }
        public TPosition? LastProcessedPosition { get; set; }
        public CheckpointTimer CheckpointTimer { get; }
        public int CheckpointEventCount { get; set; }
        public CheckpointFrequency CheckpointFrequency { get; set; }
    }

    private sealed class CheckpointTimer : IDisposable
    {
        private readonly IEventStreamConsumer<TEvent, TPosition> _consumer;
        private TimeSpan _duration;
        private CancellationTokenSource _cancellationTokenSource = new();

        public CheckpointTimer(IEventStreamConsumer<TEvent, TPosition> consumer, TimeSpan duration)
        {
            _consumer = consumer;
            _duration = duration;

            Task = System.Threading.Tasks.Task
                .Delay(duration, _cancellationTokenSource.Token)
                .ContinueWith(_ => _consumer);
        }

        public Task<IEventStreamConsumer<TEvent, TPosition>> Task { get; private set; }

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
                .ContinueWith(_ => _consumer);
        }

        public void Dispose()
        {
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
        }
    }
}