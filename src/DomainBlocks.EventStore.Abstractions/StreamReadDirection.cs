namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// Specifies the direction in which a stream is read.
/// </summary>
public enum StreamReadDirection
{
    /// <summary>
    /// Forward through the stream, from the first event to the last.
    /// </summary>
    Forward = 0,

    /// <summary>
    /// Backward through the stream, from the last event to the first.
    /// </summary>
    Backward
}