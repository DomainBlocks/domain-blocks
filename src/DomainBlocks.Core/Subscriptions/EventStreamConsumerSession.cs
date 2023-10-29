using DomainBlocks.Core.Subscriptions.Concurrency;

namespace DomainBlocks.Core.Subscriptions;

internal sealed class EventStreamConsumerSession<TEvent, TPosition> : IDisposable
    where TPosition : struct, IComparable<TPosition>
{
    private readonly IEventStreamConsumer<TEvent, TPosition> _consumer;
    private readonly CheckpointTimer _checkpointTimer;
    private TPosition? _startPosition;
    private TPosition? _lastProcessedPosition;
    private int _checkpointEventCount;
    private CheckpointFrequency _checkpointFrequency = CheckpointFrequency.Default;

    public EventStreamConsumerSession(
        IEventStreamConsumer<TEvent, TPosition> consumer, ArenaQueue<QueueNotification<TEvent, TPosition>> queue)
    {
        _consumer = consumer;
        _checkpointTimer =
            new CheckpointTimer(ct => queue.WriteAsync(x => x.SetCheckpointTimerElapsed(Id), ct));
    }

    public Guid Id { get; } = Guid.NewGuid();

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
                .ContinueWith(async t =>
                {
                    await t;
                    await _onElapsed(_cancellationTokenSource.Token);
                });
        }

        public void Dispose()
        {
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
        }
    }
}