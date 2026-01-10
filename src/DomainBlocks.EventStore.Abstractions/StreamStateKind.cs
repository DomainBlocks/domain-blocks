namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// Specifies the kind of state observed for an event stream.
/// </summary>
public enum StreamStateKind
{
    StreamDoesNotExist = 0,
    StreamExists
}