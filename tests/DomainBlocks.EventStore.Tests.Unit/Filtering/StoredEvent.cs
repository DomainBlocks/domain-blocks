using DomainBlocks.EventStore.Filtering;

namespace DomainBlocks.EventStore.Tests.Unit.Filtering;

/// <summary>
/// An event as a store holds it, for evaluating filters against.
/// </summary>
internal sealed record StoredEvent : IFilterableEvent
{
    public string EventName { get; init; } = "OrderPlaced";

    public string StreamId { get; init; } = "order-1";

    public DateTimeOffset CreatedAt { get; init; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public Dictionary<string, string> Metadata { get; init; } = [];

    public object DecodedPayload { get; init; } = new();

    /// <summary>
    /// The payload as it is stored, if it is stored as JSON. A filter in process never looks at it: only a database
    /// does, which <see cref="EventFilterOracle"/> stands in for.
    /// </summary>
    public string? StoredPayload { get; init; }

    public bool TryGetMetadata(string key, out string value) => Metadata.TryGetValue(key, out value!);
}