namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// Specifies the kind of expected state for a stream.
/// </summary>
public enum ExpectedStreamStateKind
{
    /// <summary>
    /// Any state; stream may exist at any version or may not exist.
    /// </summary>
    Any = 0,

    /// <summary>
    /// Stream must exist.
    /// </summary>
    StreamExists,

    /// <summary>
    /// Stream must not exist.
    /// </summary>
    StreamDoesNotExist,

    /// <summary>
    /// Stream must be at a specific version.
    /// </summary>
    SpecificVersion
}