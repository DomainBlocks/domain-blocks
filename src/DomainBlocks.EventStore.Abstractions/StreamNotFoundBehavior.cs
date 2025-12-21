namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// Defines how to handle attempts to read a stream that does not exist.
/// </summary>
public enum StreamNotFoundBehavior
{
    /// <summary>
    /// Treat a non-existent stream as an empty async sequence.
    /// </summary>
    Ignore,

    /// <summary>
    /// Throw an exception when the stream does not exist.
    /// </summary>
    Throw
}