namespace DomainBlocks.EventStore.Internal.Subscriptions;

/// <summary>
/// Represents the reads of one catch-up. They see every event that the subscription's observer won't receive from the
/// feed.
/// </summary>
internal interface ICatchUp<TEvent> : IDisposable where TEvent : notnull
{
    /// <summary>
    /// Reads the end position of the followed sequence, or <see langword="null"/> if it is empty.
    /// </summary>
    ValueTask<long?> ReadEndAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads the followed sequence from one position to another. A start of -1 reads from the start of the sequence.
    /// </summary>
    IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadAsync(
        long startExclusive,
        long endInclusive,
        CancellationToken cancellationToken);
}