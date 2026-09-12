namespace DomainBlocks.EventStore.PostgreSQL.Feeds;

/// <summary>
/// Establishes a new <see cref="IEventLogSession"/>.
/// </summary>
internal delegate Task<IEventLogSession> EventLogSessionFactory(CancellationToken cancellationToken);