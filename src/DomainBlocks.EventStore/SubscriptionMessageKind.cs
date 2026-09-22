namespace DomainBlocks.EventStore;

/// <summary>
/// Identifies what a <see cref="SubscriptionMessage{TEvent,TStreamId,TStreamPos,TLogPos}"/> carries.
/// </summary>
public enum SubscriptionMessageKind
{
    /// <summary>
    /// An event read from the store.
    /// </summary>
    Event,

    /// <summary>
    /// The subscription has delivered every event that existed when it started, or when it last fell behind, and is
    /// now delivering live events.
    /// </summary>
    CaughtUp,

    /// <summary>
    /// The subscriber consumed events too slowly, or the store lost its live feed, and events may have been skipped.
    /// The subscription resumes from the last delivered position and emits <see cref="CaughtUp"/> once it has caught
    /// up again.
    /// </summary>
    FellBehind,

    /// <summary>
    /// The subscription has delivered every event it selects up to a position, which may be past the last event it
    /// delivered. Only a subscription with a filter reports these.
    /// </summary>
    Checkpoint
}