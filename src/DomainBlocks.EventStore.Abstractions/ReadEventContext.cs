namespace DomainBlocks.EventStore.Abstractions;

public readonly struct ReadEventContext(
    string streamId,
    StreamVersion streamVersion,
    DateTime createdAtUtc,
    GlobalPosition? globalPosition = null)
{
    public string StreamId { get; } = streamId;
    public StreamVersion StreamVersion { get; } = streamVersion;
    public DateTime CreatedAtUtc { get; } = createdAtUtc;
    public GlobalPosition? GlobalPosition { get; } = globalPosition;
}