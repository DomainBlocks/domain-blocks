namespace DomainBlocks.EventStore.Internal.Subscriptions;

/// <summary>
/// Represents the history of a followed sequence. It is the counterpart to a feed: a subscription replays from it what
/// the feed won't deliver.
/// </summary>
internal interface ICatchUpSource<TEvent, TMark> where TEvent : notnull
{
    /// <summary>
    /// Starts a catch-up whose reads see every event up to the feed's high-water mark.
    /// </summary>
    ValueTask<ICatchUp<TEvent>> StartAsync(
        TMark highWaterMark,
        string correlationId,
        CancellationToken cancellationToken);
}