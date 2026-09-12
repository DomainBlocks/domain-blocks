namespace DomainBlocks.EventStore.PostgreSQL.Feeds;

/// <summary>
/// Establishes a new <see cref="IEventLogSession{T}"/>.
/// </summary>
internal delegate Task<IEventLogSession<T>> EventLogSessionFactory<T>(CancellationToken cancellationToken);