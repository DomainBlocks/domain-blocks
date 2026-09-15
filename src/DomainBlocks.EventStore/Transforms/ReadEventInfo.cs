namespace DomainBlocks.EventStore.Transforms;

/// <summary>
/// The store-independent part of a read event's context, for transforms that do not need stream or log positions.
/// </summary>
public readonly struct ReadEventInfo(IReadOnlyDictionary<string, string> metadata, DateTimeOffset createdAt)
{
    public IReadOnlyDictionary<string, string> Metadata { get; } = metadata;
    public DateTimeOffset CreatedAt { get; } = createdAt;
}