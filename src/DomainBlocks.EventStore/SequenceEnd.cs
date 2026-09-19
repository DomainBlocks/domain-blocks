namespace DomainBlocks.EventStore;

/// <summary>
/// The end of an event sequence, i.e., an individual event stream or the global event log. A case of both
/// <see cref="ReadOrigin{TPos}"/> and <see cref="SubscriptionOrigin{TPos}"/>.
/// </summary>
public readonly record struct SequenceEnd;