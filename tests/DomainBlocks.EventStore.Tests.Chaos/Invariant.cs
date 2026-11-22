namespace DomainBlocks.EventStore.Tests.Chaos;

/// <summary>
/// Specifies an invariant that a run must keep, whatever faults it meets.
/// </summary>
internal enum Invariant
{
    /// <summary>
    /// The event log's positions increase by one from each event to the next.
    /// </summary>
    LogIsContiguous,

    /// <summary>
    /// Each stream's positions increase by one from each of its events to the next, in log order.
    /// </summary>
    StreamsAreContiguous,

    /// <summary>
    /// No event that the writer appended is in the event log more than once.
    /// </summary>
    NoEventIsDuplicated,

    /// <summary>
    /// Every event of every operation that returned is in the event log.
    /// </summary>
    NoAcknowledgedAppendIsLost,

    /// <summary>
    /// The subscriber received the event log's events in log order, with none skipped, repeated, or added.
    /// </summary>
    SubscriberMatchesLog,

    /// <summary>
    /// The subscriber received every event in the event log.
    /// </summary>
    SubscriberCaughtUp,

    /// <summary>
    /// The subscription didn't fail.
    /// </summary>
    SubscriptionKeptRunning
}