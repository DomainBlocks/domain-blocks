namespace DomainBlocks.EventStore.Transforms;

/// <summary>
/// Represents the store-independent part of a read event's context, for transforms that need no positions.
/// </summary>
public readonly struct ReadEventInfo(IReadOnlyDictionary<string, string> metadata, DateTimeOffset createdAt)
{
    public IReadOnlyDictionary<string, string> Metadata { get; } = metadata;
    public DateTimeOffset CreatedAt { get; } = createdAt;
}