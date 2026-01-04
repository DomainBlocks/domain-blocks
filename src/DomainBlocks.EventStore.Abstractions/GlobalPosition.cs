namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// A store-assigned, totally ordered position across all streams, if supported by the underlying event store.
/// </summary>
public readonly record struct GlobalPosition(ulong Value);