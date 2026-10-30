namespace DomainBlocks.EventStore.Internal.Subscriptions;

/// <summary>
/// Represents the reads of one catch-up. They see every event that the subscription's observer won't receive from the
/// feed.
/// </summary>
internal interface ICatchUp<TEvent, TPos> : IDisposable
    where TEvent : notnull
    where TPos : struct, IPosition<TPos>
{
    /// <summary>
    /// Reads the end position of the followed sequence, or <see langword="null"/> if it is empty.
    /// </summary>
    ValueTask<TPos?> ReadEndAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads the followed sequence from one position to another.
    /// </summary>
    IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadAsync(
        long startExclusive,
        long endInclusive,
        CancellationToken cancellationToken);
}