namespace DomainBlocks.EventStore.PostgreSQL.Feeds;

/// <summary>
/// One connection to the source of live event log items. It is the only part of the feed that knows the transport and
/// how an item is decoded.
/// </summary>
/// <remarks>
/// Once the factory completes, <see cref="ReadAsync"/> yields every row committed after the session's establishment
/// point, in commit order, until the session faults or is disposed. Uncommitted rows are never yielded.
/// </remarks>
internal interface IEventLogSession<out T> : IAsyncDisposable
{
    /// <summary>
    /// A short description for diagnostics, e.g., the replication slot name.
    /// </summary>
    string Description { get; }

    IAsyncEnumerable<T> ReadAsync(CancellationToken cancellationToken);
}