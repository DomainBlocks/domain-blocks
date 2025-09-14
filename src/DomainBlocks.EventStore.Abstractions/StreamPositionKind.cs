namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// Represents a position within a stream when reading events.
/// </summary>
public enum StreamPositionKind
{
    /// <summary>
    /// Logical position before the first event in the stream.
    /// </summary>
    Start = 0,

    /// <summary>
    /// Logical position after the latest event in the stream.
    /// </summary>
    End,

    /// <summary>
    /// The position at a specific version in the stream.
    /// </summary>
    Specific
}