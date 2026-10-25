namespace DomainBlocks.EventStore.PostgreSQL.Feeds;

/// <summary>
/// The result of preparing a feed's replication slot for the feed's next session.
/// </summary>
internal enum PrepareForSessionResult
{
    /// <summary>
    /// A session can stream from the slot.
    /// </summary>
    Ready,

    /// <summary>
    /// The slot has been dropped or invalidated.
    /// </summary>
    Lost,

    /// <summary>
    /// Another process still holds the slot.
    /// </summary>
    InUse
}