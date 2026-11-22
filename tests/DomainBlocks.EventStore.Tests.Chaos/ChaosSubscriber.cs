namespace DomainBlocks.EventStore.Tests.Chaos;

/// <summary>
/// Subscribes to all streams from the start, and records every event that it receives.
/// </summary>
internal sealed class ChaosSubscriber : IAsyncDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(20);

    private readonly List<ObservedEvent> _received = [];
    private readonly Lock _lock = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _receiving;
    private int _fellBehindCount;

    public ChaosSubscriber(IEventStore<object, string, StreamPosition, LogPosition> eventStore)
    {
        _receiving = ReceiveAsync(eventStore, _stopping.Token);
    }

    public IReadOnlyList<ObservedEvent> Received
    {
        get
        {
            lock (_lock)
                return [.. _received];
        }
    }

    public int FellBehindCount => Volatile.Read(ref _fellBehindCount);

    /// <summary>
    /// Gets the error that ended the subscription, or <see langword="null"/> if none has.
    /// </summary>
    public Exception? Error { get; private set; }

    /// <summary>
    /// Waits until the subscriber has received the event at <paramref name="position"/>, the subscription ends, or the
    /// timeout elapses.
    /// </summary>
    public async Task WaitForAsync(LogPosition position, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            while (!HasReceived(position) && !_receiving.IsCompleted)
                await Task.Delay(PollInterval, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The checker reports the events that the subscriber didn't receive.
        }
    }

    public async Task StopAsync()
    {
        await _stopping.CancelAsync();
        await _receiving;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _stopping.Dispose();
    }

    private bool HasReceived(LogPosition position)
    {
        lock (_lock)
            return _received.Count > 0 && _received[^1].LogPosition.Value >= position.Value;
    }

    private async Task ReceiveAsync(
        IEventStore<object, string, StreamPosition, LogPosition> eventStore,
        CancellationToken cancellationToken)
    {
        // The constructor returns before the subscription starts.
        await Task.Yield();

        try
        {
            var messages = eventStore.SubscribeToAll(SubscriptionOrigin.Start).WithCancellation(cancellationToken);

            await foreach (var message in messages)
            {
                switch (message)
                {
                    case { Event: { } readEvent }:
                        lock (_lock)
                            _received.Add(ObservedEvent.From(readEvent));
                        break;
                    case { IsFellBehind: true }:
                        Interlocked.Increment(ref _fellBehindCount);
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Stopped by StopAsync.
        }
        catch (Exception ex)
        {
            Error = ex;
        }
    }
}