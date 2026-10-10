namespace DomainBlocks.EventStore;

/// <summary>
/// Specifies the kind of an <see cref="ObservedStreamState{TVersion}"/>.
/// </summary>
public enum ObservedStreamStateKind
{
    DoesNotExist = 0,
    AtVersion
}