namespace DomainBlocks.EventStore;

/// <summary>
/// Specifies what a stream read does if the stream does not exist.
/// </summary>
public enum StreamNotFoundBehavior
{
    /// <summary>
    /// Return no events.
    /// </summary>
    Ignore,

    /// <summary>
    /// Throw a <see cref="StreamNotFoundException"/>.
    /// </summary>
    Throw
}