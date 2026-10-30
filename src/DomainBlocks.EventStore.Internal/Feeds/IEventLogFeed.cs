namespace DomainBlocks.EventStore.Internal.Feeds;

/// <summary>
/// A live feed of items that fans out to any number of observers.
/// </summary>
internal interface IEventLogFeed<out T, THighWaterMark>
{
    IDisposable Attach(IFeedObserver<T> observer, string correlationId = "unknown");

    Task<IFeedConnection<THighWaterMark>> ConnectAsync(CancellationToken cancellationToken = default);
}