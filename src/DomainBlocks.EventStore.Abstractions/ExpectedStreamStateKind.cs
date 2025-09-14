namespace DomainBlocks.EventStore.Abstractions;

public enum ExpectedStreamStateKind
{
    Any = 0,
    StreamExists,
    StreamDoesNotExist,
    SpecificVersion
}