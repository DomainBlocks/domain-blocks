namespace DomainBlocks.EventStore;

/// <summary>
/// Specifies the kind of an <see cref="ExpectedStreamState{TVersion}"/>.
/// </summary>
public enum ExpectedStreamStateKind
{
    Any = 0,
    DoesNotExist,
    Exists,
    AtVersion
}