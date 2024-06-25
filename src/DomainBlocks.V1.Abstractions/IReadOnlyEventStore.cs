using DomainBlocks.V1.Abstractions.Subscriptions;

namespace DomainBlocks.V1.Abstractions;

/// <summary>
/// Exposes read-only operations for a store of events.
/// </summary>
public interface IReadOnlyEventStore
{
    Task<StreamLoadResult> ReadStreamAsync(
        string streamName,
        StreamReadDirection direction,
        StreamPosition fromPosition,
        CancellationToken cancellationToken = default);

    Task<StreamLoadResult> ReadStreamAsync(
        string streamName,
        StreamReadDirection direction,
        StreamReadOrigin readOrigin = StreamReadOrigin.Default,
        CancellationToken cancellationToken = default);

    IEventStreamSubscription SubscribeToAll(GlobalPosition? afterPosition = null);

    IEventStreamSubscription SubscribeToStream(string streamName, StreamPosition? afterPosition = null);
}