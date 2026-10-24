namespace DomainBlocks.EventStore.PostgreSQL.Feeds;

/// <summary>
/// Opens a feed's sessions, one at a time. Disposing it releases what the sessions share, such as a replication slot.
/// </summary>
internal interface IEventLogSessionFactory<T> : IAsyncDisposable
{
    Task<IEventLogSession<T>> OpenAsync(CancellationToken cancellationToken);
}