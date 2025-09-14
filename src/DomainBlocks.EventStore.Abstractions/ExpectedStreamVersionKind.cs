namespace DomainBlocks.EventStore.Abstractions;

public enum ExpectedStreamVersionKind
{
    Any = 0,
    Exists,
    None,
    Specific
}