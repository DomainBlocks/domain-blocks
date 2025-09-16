namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// Specifies the outcome of a stream read operation.
/// </summary>
public enum ReadStreamStatus
{
    /// <summary>
    /// The read is successful.
    /// </summary>
    Success,

    /// <summary>
    /// The stream does not exist.
    /// </summary>
    StreamNotFound
}