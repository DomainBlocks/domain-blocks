namespace DomainBlocks.EventStore.Abstractions;

public readonly struct ReadEventContext(
    string streamId,
    StreamVersion streamVersion,
    DateTime createdAtUtc,
    LogPosition? globalPosition = null)
{
    public string StreamId { get; } = streamId;
    public StreamVersion StreamVersion { get; } = streamVersion;
    public DateTime CreatedAtUtc { get; } = createdAtUtc;
    public LogPosition? GlobalPosition { get; } = globalPosition;
}