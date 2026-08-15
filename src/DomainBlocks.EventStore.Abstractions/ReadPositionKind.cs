namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// Specifies the kind of a read position: the start or end of an event sequence, or a specific position within it.
/// </summary>
public enum ReadPositionKind
{
    Start = 0,
    End,
    Specific
}