namespace DomainBlocks.EventStore;

/// <summary>
/// The state of an event stream that does not exist. A case of both <see cref="ExpectedStreamState{TVersion}"/> and
/// <see cref="ObservedStreamState{TVersion}"/>.
/// </summary>
public readonly record struct StreamDoesNotExist;