namespace DomainBlocks.EventStore;

/// <summary>
/// Reports that a subscription has delivered every event that existed when it started, or when it last fell behind,
/// and is now delivering live events. A case of <see cref="SubscriptionMessage{TEvent,TStreamId,TStreamPos,TLogPos}"/>.
/// </summary>
public readonly record struct SubscriptionCaughtUp;