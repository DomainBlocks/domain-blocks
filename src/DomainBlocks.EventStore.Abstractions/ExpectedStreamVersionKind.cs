namespace DomainBlocks.EventStore.Abstractions;

public enum ExpectedStreamVersionKind
{
    Any = 0, // Any is default
    Exists,
    None,
    Specific
}