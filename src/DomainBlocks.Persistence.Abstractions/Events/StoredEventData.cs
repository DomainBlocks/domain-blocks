namespace DomainBlocks.Persistence.Abstractions.Events;

public sealed class StoredEventData<TPayload>(
    string streamId,
    long streamVersion,
    string eventName,
    TPayload payload,
    IReadOnlyDictionary<string, string> metadata,
    DateTimeOffset committedAt,
    long? globalPosition = null)
{
    public string StreamId { get; } = streamId;
    public long StreamVersion { get; } = streamVersion;
    public string EventName { get; } = eventName;
    public TPayload Payload { get; } = payload;
    public IReadOnlyDictionary<string, string> Metadata { get; } = metadata;
    public DateTimeOffset CommittedAt { get; } = committedAt;
    public long? GlobalPosition { get; } = globalPosition;
}