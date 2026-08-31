namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// Specifies the kind of state observed for an event stream.
/// </summary>
public enum ObservedStreamStateKind
{
    DoesNotExist = 0,
    AtVersion
}