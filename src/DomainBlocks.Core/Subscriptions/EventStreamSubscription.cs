using DomainBlocks.Core.Subscriptions.Concurrency;

namespace DomainBlocks.Core.Subscriptions;

public class EventStreamSubscription<TEvent, TPosition> :
    IEventStreamSubscription,
    IEventStreamListener<TEvent, TPosition>
    where TPosition : struct, IEquatable<TPosition>, IComparable<TPosition>
{
    private const int DefaultNotificationQueueSize = 1;

    private readonly IEventStreamSubscriber<TEvent, TPosition> _subscriber;
    private readonly ConsumerSessionGroup _sessions;
    private readonly ArenaQueue<Notification<TEvent, TPosition>> _queue = new(DefaultNotificationQueueSize);

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
            await foreach (var notification in _queue.TakeAllAsync(_cancellationTokenSource.Token))
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

    private async Task HandleNotification(Notification<TEvent, TPosition> notification)
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

    private sealed class ConsumerSession : IDisposable
    {
        private readonly IEventStreamConsumer<TEvent, TPosition> _consumer;
        private readonly CheckpointTimer _checkpointTimer;
        private TPosition? _startPosition;
        private TPosition? _lastProcessedPosition;
        private int _checkpointEventCount;
        private CheckpointFrequency _checkpointFrequency = CheckpointFrequency.Default;

        public ConsumerSession(
            IEventStreamConsumer<TEvent, TPosition> consumer, ArenaQueue<Notification<TEvent, TPosition>> queue)
        {
            _consumer = consumer;
            _checkpointTimer =
                new CheckpointTimer(ct => queue.PutAsync(x => x.SetCheckpointTimerElapsed(consumer), ct));
        }

        public void Dispose()
        {
            _checkpointTimer.Dispose();
        }

        public async Task<TPosition?> NotifyStarting(CancellationToken cancellationToken)
        {
            _startPosition = await _consumer.OnStarting(cancellationToken);
            return _startPosition;
        }

        public Task NotifyCatchingUp(CancellationToken cancellationToken)
        {
            _checkpointFrequency = _consumer.CatchUpCheckpointFrequency;
            return _consumer.OnCatchingUp(cancellationToken);
        }

        public async Task NotifyEvent(TEvent @event, TPosition position, CancellationToken cancellationToken)
        {
            if (_startPosition != null && position.CompareTo(_startPosition.Value) < 1)
            {
                // This consumer's start position is ahead of this event's position. Ignore.
                return;
            }

            var isHandled = false;
            var result = OnEventResult.Ignored;

            do
            {
                try
                {
                    result = await _consumer.OnEvent(@event, position, cancellationToken);
                    isHandled = true;
                    break;
                }
                catch (Exception ex)
                {
                    var resolution = await _consumer.OnEventError(@event, position, ex, cancellationToken);

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
                _lastProcessedPosition = position;
                _checkpointEventCount += _checkpointFrequency.CanCheckpoint ? 1 : 0;

                if (_checkpointEventCount == _checkpointFrequency.PerEventCount)
                {
                    await _consumer.OnCheckpoint(position, cancellationToken);
                    _checkpointEventCount = 0;
                    ResetCheckpointTimer();
                }
            }
        }

        public Task NotifyLive(CancellationToken cancellationToken)
        {
            _checkpointFrequency = _consumer.LiveCheckpointFrequency;
            return _consumer.OnLive(cancellationToken);
        }

        public Task NotifySubscriptionDropped(
            SubscriptionDroppedReason reason, Exception? exception, CancellationToken cancellationToken)
        {
            return _consumer.OnSubscriptionDropped(reason, exception, cancellationToken);
        }

        public async Task NotifyCheckpoint(CancellationToken cancellationToken)
        {
            if (_checkpointEventCount > 0)
            {
                await _consumer.OnCheckpoint(_lastProcessedPosition!.Value, cancellationToken);
                _checkpointEventCount = 0;
            }
        }

        public void ResetCheckpointTimer()
        {
            var duration = _checkpointFrequency.PerTimeInterval ?? Timeout.InfiniteTimeSpan;
            _checkpointTimer.Reset(duration);
        }
    }

    private sealed class ConsumerSessionGroup : IDisposable
    {
        private readonly IReadOnlyDictionary<IEventStreamConsumer<TEvent, TPosition>, ConsumerSession> _sessions;

        public ConsumerSessionGroup(
            IEnumerable<IEventStreamConsumer<TEvent, TPosition>> consumers,
            ArenaQueue<Notification<TEvent, TPosition>> queue)
        {
            _sessions = consumers.ToDictionary(x => x, x => new ConsumerSession(x, queue));
        }

        public void Dispose()
        {
            foreach (var session in _sessions.Values)
            {
                session.Dispose();
            }
        }

        public async Task<TPosition?> NotifyStarting(CancellationToken cancellationToken)
        {
            var tasks = _sessions.Values.Select(x => x.NotifyStarting(cancellationToken));
            var startPositions = await Task.WhenAll(tasks);
            return startPositions.Min();
        }

        public async Task NotifyCatchingUp(CancellationToken cancellationToken)
        {
            var tasks = _sessions.Values.Select(x => x.NotifyCatchingUp(cancellationToken));
            await Task.WhenAll(tasks);

            foreach (var session in _sessions.Values)
            {
                session.ResetCheckpointTimer();
            }
        }

        public Task NotifyEvent(TEvent @event, TPosition position, CancellationToken cancellationToken)
        {
            var tasks = _sessions.Values.Select(x => x.NotifyEvent(@event, position, cancellationToken));
            return Task.WhenAll(tasks);
        }

        public async Task NotifyLive(CancellationToken cancellationToken)
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

        public Task NotifySubscriptionDropped(
            SubscriptionDroppedReason reason, Exception? exception, CancellationToken cancellationToken)
        {
            var tasks = _sessions.Values.Select(x => x.NotifySubscriptionDropped(reason, exception, cancellationToken));
            return Task.WhenAll(tasks);
        }

        public async Task NotifyCheckpointTimerElapsed(
            IEventStreamConsumer<TEvent, TPosition> consumer, CancellationToken cancellationToken)
        {
            var session = _sessions[consumer];
            await session.NotifyCheckpoint(cancellationToken);
            session.ResetCheckpointTimer();
        }
    }

    private sealed class CheckpointTimer : IDisposable
    {
        private readonly Func<CancellationToken, Task> _onElapsed;
        private TimeSpan _duration = Timeout.InfiniteTimeSpan;
        private CancellationTokenSource _cancellationTokenSource = new();
        private Task _task;

        public CheckpointTimer(Func<CancellationToken, Task> onElapsed)
        {
            _onElapsed = onElapsed;
            _task = Task.Delay(_duration, _cancellationTokenSource.Token);
        }

        public void Reset(TimeSpan duration)
        {
            _duration = duration;

            if (!_task.IsCompletedSuccessfully || !_cancellationTokenSource.TryReset())
            {
                _cancellationTokenSource.Cancel();
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = new CancellationTokenSource();
            }

            _task = Task
                .Delay(_duration, _cancellationTokenSource.Token)
                .ContinueWith(_ => _onElapsed(_cancellationTokenSource.Token));
        }

        public void Dispose()
        {
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
        }
    }

    // IEventStreamListener members
    Task IEventStreamListener<TEvent, TPosition>.OnCatchingUp(CancellationToken cancellationToken)
    {
        return _queue.PutAsync(x => x.SetCatchingUp(), cancellationToken);
    }

    async Task<OnEventResult> IEventStreamListener<TEvent, TPosition>.OnEvent(
        TEvent @event, TPosition position, CancellationToken cancellationToken)
    {
        await _queue.PutAsync(x => x.SetEvent(@event, position), cancellationToken);
        return OnEventResult.Processed;
    }

    Task IEventStreamListener<TEvent, TPosition>.OnLive(CancellationToken cancellationToken)
    {
        return _queue.PutAsync(x => x.SetLive(), cancellationToken);
    }

    Task IEventStreamListener<TEvent, TPosition>.OnSubscriptionDropped(
        SubscriptionDroppedReason reason, Exception? exception, CancellationToken cancellationToken)
    {
        return _queue.PutAsync(x => x.SetSubscriptionDropped(reason, exception), cancellationToken);
    }
}