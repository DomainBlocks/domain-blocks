namespace DomainBlocks.Core.Subscriptions;

public partial class EventStreamSubscription<TEvent, TPosition>
{
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