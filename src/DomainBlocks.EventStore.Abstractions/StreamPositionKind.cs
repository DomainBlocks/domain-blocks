namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// Specifies the kind of position within a stream.
/// </summary>
public enum StreamPositionKind
{
    /// <summary>
    /// Logical position at the start of the stream, before the first committed event.
    /// </summary>
    Start = 0,

    /// <summary>
    /// Logical position at the end of the stream, after the last committed event.
    /// </summary>
    End,

    /// <summary>
    /// A position at a specific committed event version.
    /// </summary>
    SpecificVersion
}