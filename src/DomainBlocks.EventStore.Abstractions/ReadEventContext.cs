namespace DomainBlocks.EventStore.Abstractions;

public readonly record struct ReadEventContext(
    string StreamId,
    StreamVersion StreamVersion,
    DateTime CreatedAtUtc,
    LogSequenceNumber? LogSequenceNumber = null);