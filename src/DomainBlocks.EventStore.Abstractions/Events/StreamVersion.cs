namespace DomainBlocks.EventStore.Abstractions.Events;

/// <summary>
/// Represents the version of a stream. Versions are zero-based, increasing with each appended event.
/// </summary>
public readonly record struct StreamVersion(ulong Value);