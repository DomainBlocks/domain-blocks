namespace DomainBlocks.EventStore.Internal.Subscriptions;

/// <summary>
/// Represents the reads of a followed sequence's history. It is the counterpart to a feed: a subscription replays from
/// it what the feed won't deliver.
/// </summary>
internal interface ICatchUpReader<TEvent, TMark> where TEvent : notnull
{
    /// <summary>
    /// Reads the end position of the followed sequence, or <see langword="null"/> if it is empty. The end covers every
    /// event up to the mark.
    /// </summary>
    /// <returns>The end position, and the mark to read up to that end with.</returns>
    ValueTask<(long? End, TMark Mark)> ReadEndAsync(
        TMark mark,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads the followed sequence from one position to another. A start of -1 reads from the start of the sequence.
    /// </summary>
    /// <param name="mark">The mark from the read of <paramref name="endInclusive"/>.</param>
    /// <param name="startExclusive">The position to read after.</param>
    /// <param name="endInclusive">The last position to read.</param>
    /// <param name="cancellationToken">The token to cancel the read with.</param>
    IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadAsync(
        TMark mark,
        long startExclusive,
        long endInclusive,
        CancellationToken cancellationToken);
}