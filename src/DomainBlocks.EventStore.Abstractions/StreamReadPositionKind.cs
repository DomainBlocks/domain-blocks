namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// Specifies the kind of position from which a stream read begins.
/// </summary>
public enum StreamReadPositionKind
{
    Start = 0,
    End,
    SpecificVersion
}