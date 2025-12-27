namespace DomainBlocks.EventStore.Abstractions.Events;

public sealed class ReadEventHeader(
    string streamId,
    StreamVersion streamVersion,
    string eventName,
    IReadOnlyDictionary<string, string> metadata,
    DateTimeOffset createdAt,
    GlobalPosition? globalPosition = null)
{
    public string StreamId { get; } = streamId;
    public StreamVersion StreamVersion { get; } = streamVersion;
    public string EventName { get; } = eventName;
    public IReadOnlyDictionary<string, string> Metadata { get; } = metadata;
    public DateTimeOffset CreatedAt { get; } = createdAt;
    public GlobalPosition? GlobalPosition { get; } = globalPosition;
}