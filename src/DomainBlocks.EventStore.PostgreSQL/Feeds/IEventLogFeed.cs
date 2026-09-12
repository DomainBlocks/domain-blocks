namespace DomainBlocks.EventStore.PostgreSQL.Feeds;

/// <summary>
/// A single live feed of event log items that fans out to any number of observers.
/// </summary>
internal interface IEventLogFeed<out T>
{
    IDisposable Attach(IEventLogObserver<T> observer, string correlationId = "unknown");

    Task<IEventLogFeedConnection> ConnectAsync(CancellationToken cancellationToken = default);
}