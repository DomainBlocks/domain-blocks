using DomainBlocks.EventStore.Filtering;

namespace DomainBlocks.EventStore.Tests.Unit.Filtering;

internal sealed class TestFilterableEvent : IFilterableEvent
{
    public string EventName { get; init; } = "OrderPlaced";

    public string StreamId { get; init; } = "order-1";

    public DateTimeOffset CreatedAt { get; init; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public Dictionary<string, string> Metadata { get; init; } = [];

    public bool TryGetMetadata(string key, out ReadOnlySpan<char> value)
    {
        var found = Metadata.TryGetValue(key, out var entry);
        value = entry;
        return found;
    }
}