namespace DomainBlocks.EventStore.Abstractions;

public readonly record struct ReadEventContext(
    string StreamId,
    StreamPosition StreamVersion,
    DateTime CreatedAtUtc,
    LogPosition? LogSequenceNumber = null);