namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// A row of the event log as read from the database or received from the replication feed.
/// </summary>
internal sealed record EventLogRow(
    long Position,
    string StreamId,
    long StreamPosition,
    string EventName,
    PostgresEventData EventData,
    string? Metadata,
    DateTimeOffset CreatedAt);
