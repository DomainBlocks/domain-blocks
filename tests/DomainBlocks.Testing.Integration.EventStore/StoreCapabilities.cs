namespace DomainBlocks.Testing.Integration.EventStore;

/// <summary>
/// Optional behaviour a store may or may not offer. Contract tests that need one call
/// <c>RequireCapability</c>, so that a store without it reports the test as ignored rather than failing.
/// </summary>
[Flags]
public enum StoreCapabilities
{
    None = 0,

    /// <summary>
    /// Appending with a commit id that was already committed writes nothing and succeeds.
    /// </summary>
    IdempotentAppends = 1,

    /// <summary>
    /// Reads take an event filter. A store without it refuses one.
    /// </summary>
    EventFilters = 2,

    /// <summary>
    /// Subscriptions take an event filter. A store without it refuses one.
    /// </summary>
    EventFilteredSubscriptions = 4,

    /// <summary>
    /// Subscriptions with a filter report checkpoints.
    /// </summary>
    SubscriptionCheckpoints = 8
}