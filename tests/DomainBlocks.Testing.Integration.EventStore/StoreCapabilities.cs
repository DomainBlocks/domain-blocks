namespace DomainBlocks.Testing.Integration.EventStore;

/// <summary>
/// Optional behavior that a store may or may not offer. A contract test that needs one calls <c>RequireCapability</c>,
/// so a store without it reports the test as ignored rather than failing it.
/// </summary>
[Flags]
public enum StoreCapabilities
{
    None = 0,

    /// <summary>
    /// Appending with a commit ID that was already committed writes nothing and succeeds.
    /// </summary>
    IdempotentAppends = 1,

    /// <summary>
    /// Reads select events with a filter given in their options.
    /// </summary>
    FilteredReads = 2,

    /// <summary>
    /// Subscriptions select events with a filter given in their options.
    /// </summary>
    FilteredSubscriptions = 4
}