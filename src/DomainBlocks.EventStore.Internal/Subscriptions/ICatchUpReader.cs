namespace DomainBlocks.EventStore.Internal.Subscriptions;

/// <summary>
/// Represents the reads of a followed sequence's history. It is the counterpart to a feed: a subscription replays from
/// it what the feed won't deliver.
/// </summary>
internal interface ICatchUpReader<TEvent, in TMark, TPos>
    where TEvent : notnull
    where TPos : struct, IPosition<TPos>
{
    /// <summary>
    /// Reads a snapshot of the followed sequence from the origin, which covers every event up to the feed's high-water
    /// mark.
    /// </summary>
    ValueTask<CatchUpSnapshot<TEvent, TPos>> ReadSnapshotAsync(
        TMark highWaterMark,
        SubscriptionOrigin<TPos> origin,
        string correlationId,
        CancellationToken cancellationToken);
}