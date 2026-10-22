namespace DomainBlocks.EventStore.PostgreSQL.Feeds;

/// <summary>
/// One connection to the source of live event log items. It is the only part of the feed that knows the transport and
/// how an item is decoded.
/// </summary>
/// <remarks>
/// Once the factory completes, <see cref="ReadAsync"/> yields every row committed after the session's establishment
/// point, in commit order, until the session faults or is disposed. Uncommitted rows are never yielded, and no row is
/// yielded twice, even by a later session.
/// </remarks>
internal interface IEventLogSession<out T> : IAsyncDisposable
{
    /// <summary>
    /// A short description for diagnostics, e.g., the replication slot name.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Whether the session's establishment point is where the previous session of the feed stopped, so that no row was
    /// missed in between.
    /// </summary>
    bool IsResumed { get; }

    IAsyncEnumerable<T> ReadAsync(CancellationToken cancellationToken);
}