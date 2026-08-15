namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// Specifies the direction in which the event log, or an event stream, is read.
/// </summary>
public enum ReadDirection
{
    /// <summary>
    /// Read forward from the first event to the last.
    /// </summary>
    Forward = 0,

    /// <summary>
    /// Read backward from the last event to the first.
    /// </summary>
    Backward
}