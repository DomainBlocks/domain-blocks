namespace DomainBlocks.EventStore;

/// <summary>
/// Specifies the kind of a <see cref="SubscriptionMessage{TEvent,TStreamId,TStreamPos,TLogPos}"/>.
/// </summary>
public enum SubscriptionMessageKind
{
    /// <summary>
    /// The message carries an event.
    /// </summary>
    Event,

    /// <summary>
    /// The subscription has delivered every event that existed when it started or last fell behind, and now delivers
    /// live events.
    /// </summary>
    CaughtUp,

    /// <summary>
    /// The subscription fell behind, either because the subscriber was too slow or because the store lost its live
    /// feed. The subscription catches up again from where it left off and then sends <see cref="CaughtUp"/>. The two
    /// kinds therefore alternate, starting with <see cref="CaughtUp"/>.
    /// </summary>
    FellBehind
}