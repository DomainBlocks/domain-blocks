namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// Specifies the kind of position used when reading a stream.
/// </summary>
public enum StreamReadPositionKind
{
    /// <summary>
    /// Logical position at the start of the stream, before the first event.
    /// </summary>
    Start = 0,

    /// <summary>
    /// Logical position at the end of the stream, after the last event.
    /// </summary>
    End,

    /// <summary>
    /// A position at a specific event version.
    /// </summary>
    SpecificVersion
}