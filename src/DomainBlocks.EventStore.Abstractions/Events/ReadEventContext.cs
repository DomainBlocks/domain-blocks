namespace DomainBlocks.EventStore.Abstractions.Events;

public readonly struct ReadEventContext(
    string streamId,
    StreamVersion streamVersion,
    DateTimeOffset createdAt,
    GlobalPosition? globalPosition)
{
    public string StreamId { get; } = streamId;
    public StreamVersion StreamVersion { get; } = streamVersion;
    public DateTimeOffset CreatedAt { get; } = createdAt;
    public GlobalPosition? GlobalPosition { get; } = globalPosition;
}