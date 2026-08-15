namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// Specifies the kind of state expected for an event stream operation.
/// </summary>
public enum ExpectedStreamStateKind
{
    Any = 0,
    DoesNotExist,
    Exists,
    AtVersion
}