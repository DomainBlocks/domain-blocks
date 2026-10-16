namespace DomainBlocks.EventStore.Subscriptions;

/// <summary>
/// Wakes an <see cref="EventLogTail{TSourceEvent}"/> when events may have been appended. A wake only saves time: one
/// that comes early, late, twice or not at all costs at most one poll interval, never an event.
/// </summary>
internal interface IAppendSignal : IAsyncDisposable
{
    /// <summary>
    /// Waits until an append is signalled or <paramref name="timeout"/> elapses. An append signalled since the previous
    /// wait returned ends the wait at once. The signal recovers from its own failures, so this throws only when
    /// cancelled.
    /// </summary>
    ValueTask WaitAsync(TimeSpan timeout, CancellationToken cancellationToken);
}