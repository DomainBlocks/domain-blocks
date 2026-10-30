namespace DomainBlocks.EventStore.Internal.Subscriptions;

/// <summary>
/// Represents a store's reads of a followed sequence's history. It is the counterpart to a feed: a subscription replays
/// from it what the feed won't deliver.
/// </summary>
internal interface IHistoryReader<TEvent, in TMark>
    where TEvent : notnull
{
    /// <summary>
    /// Reads a snapshot of the followed sequence that covers every event up to the feed's high-water mark.
    /// </summary>
    ValueTask<IHistorySnapshot<TEvent>> ReadSnapshotAsync(
        TMark highWaterMark,
        string correlationId,
        CancellationToken cancellationToken);
}