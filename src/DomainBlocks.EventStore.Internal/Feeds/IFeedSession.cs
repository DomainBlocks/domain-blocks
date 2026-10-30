namespace DomainBlocks.EventStore.Internal.Feeds;

/// <summary>
/// Represents one connection to a store's source of live items, such as a replication session or a change stream. It
/// is the only part of a feed that knows the transport and how an item is decoded.
/// </summary>
/// <remarks>
/// Once the factory has opened the session, <see cref="ReadAsync"/> yields every item committed after the session's
/// starting point, in commit order, until the session faults or is disposed. Uncommitted items are never yielded, and
/// no item is yielded twice, even by a later session.
/// </remarks>
internal interface IFeedSession<T, THighWaterMark> : IAsyncDisposable
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
    /// re-read from their last position the items committed in between.
    /// </remarks>
    bool IsResumed { get; }

    /// <summary>
    /// Gets a mark at or after every item that the session won't yield.
    /// </summary>
    /// <remarks>
    /// The feed reads it once the session is open, before the first item, and keeps the greater of this mark and its
    /// own. With nothing to cover, a session returns <see langword="null"/> or a mark at or before the feed's, such as
    /// -1 for a position.
    /// </remarks>
    THighWaterMark? InitialHighWaterMark { get; }

    /// <summary>
    /// Reads each item with a mark at or after it. The feed raises its own mark before observers receive the item.
    /// </summary>
    IAsyncEnumerable<(T Item, THighWaterMark HighWaterMark)> ReadAsync(CancellationToken cancellationToken);
}