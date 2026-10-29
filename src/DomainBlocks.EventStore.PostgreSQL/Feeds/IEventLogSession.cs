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
    /// Gets a short description for diagnostics, such as the replication slot name.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Gets a value that indicates whether the session continues where the feed's previous session stopped.
    /// </summary>
    /// <remarks>
    /// When this is <see langword="false"/> for any session but the feed's first, the feed resets its observers, which
    /// re-read from their last position the rows committed in between.
    /// </remarks>
    bool IsResumed { get; }

    /// <summary>
    /// Gets a position at or after every row that the session has yielded, or -1 while there is nothing to cover.
    /// </summary>
    /// <remarks>
    /// The feed reads it once the session is open and after each row, before observers receive the row.
    /// </remarks>
    long HighWaterMark { get; }

    IAsyncEnumerable<T> ReadAsync(CancellationToken cancellationToken);
}