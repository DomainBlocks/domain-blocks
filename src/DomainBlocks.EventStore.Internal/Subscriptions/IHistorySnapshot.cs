namespace DomainBlocks.EventStore.Internal.Subscriptions;

/// <summary>
/// Represents a followed sequence up to the end that was read, which every read of the snapshot sees the same way. It
/// is not a snapshot in the database's sense: the log is append-only and the reads stop at the end, so what they return
/// can't change.
/// </summary>
internal interface IHistorySnapshot<TEvent>
    where TEvent : notnull
{
    /// <summary>
    /// Gets the position of the sequence's last event, or <see langword="null"/> if the sequence had no events.
    /// </summary>
    long? End { get; }

    /// <summary>
    /// Reads the followed sequence from one position to another. A start of -1 reads from the start of the sequence.
    /// </summary>
    IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadAsync(
        long startExclusive,
        long endInclusive,
        CancellationToken cancellationToken);
}