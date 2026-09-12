namespace DomainBlocks.EventStore.PostgreSQL.Feeds;

/// <summary>
/// One established connection to the source of live event log items. This is the only part of the feed that knows
/// about the transport (logical replication today) and about how an item is decoded.
/// </summary>
/// <remarks>
/// Contract: once the factory that produced the session has completed, every row committed after the session's
/// establishment point is yielded by <see cref="ReadAsync"/> in commit order, until the session faults or is
/// disposed. Rows of uncommitted transactions are never yielded.
/// </remarks>
internal interface IEventLogSession<out T> : IAsyncDisposable
{
    /// <summary>
    /// A short description of the session for diagnostics, e.g. the replication slot name.
    /// </summary>
    string Description { get; }

    IAsyncEnumerable<T> ReadAsync(CancellationToken cancellationToken);
}