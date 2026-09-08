namespace DomainBlocks.EventStore.PostgreSQL.Feeds;

/// <summary>
/// A single live feed of event log rows that fans out to any number of observers.
/// </summary>
internal interface IEventLogFeed
{
    IDisposable Attach(IEventLogObserver observer, string correlationId = "unknown");

    Task<IEventLogFeedConnection> ConnectAsync(CancellationToken cancellationToken = default);
}
