namespace DomainBlocks.EventStore;

/// <summary>
/// The state of an event stream that exists, at whatever version. A case of
/// <see cref="ExpectedStreamState{TVersion}"/>.
/// </summary>
public readonly record struct StreamExists;