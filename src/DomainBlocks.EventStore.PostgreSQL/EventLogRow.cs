namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// A row of the event log as read from the database or received from the replication feed.
/// </summary>
internal sealed class EventLogRow(
    long position,
    string streamId,
    long streamPosition,
    string eventName,
    PostgresEventData eventData,
    string? metadata,
    DateTimeOffset createdAt)
{
    public long Position { get; } = position;
    public string StreamId { get; } = streamId;
    public long StreamPosition { get; } = streamPosition;
    public string EventName { get; } = eventName;
    public PostgresEventData EventData { get; } = eventData;
    public string? Metadata { get; } = metadata;
    public DateTimeOffset CreatedAt { get; } = createdAt;
}