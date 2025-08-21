using DomainBlocks.Persistence.Abstractions.Events;
using DomainBlocks.Serialization.Events;

namespace DomainBlocks.Persistence.Events;

// Skips, splits, 1:1 mapping (e.g. upcasts, contract to domain)
public interface IEventReadTransform
{
    Type FromType { get; }
    IEnumerable<object> Apply(object @event);
}

// 1:1 mapping (e.g. domain to contract)
public interface IEventWriteTransform
{
    Type FromType { get; }
    object Apply(object @event);
}

public class EventStore<TPayload>(
    IEventDataStore<TPayload> eventDataStore,
    EventSerializer<TPayload> eventSerializer) : IEventStore
{
    public async Task AppendToStreamAsync(
        string streamId,
        IEnumerable<object> events,
        long? expectedVersion = null,
        CancellationToken cancellationToken = default)
    {
        // Pass events through write pipeline first. End of the line is serialisation.
        // Object -> pipeline -> EventData

        var eventData = events
            .Select(x =>
            {
                var (eventName, payload) = eventSerializer.Serialize(x);
                return new EventData<TPayload>(eventName, payload);
            });

        await eventDataStore.AppendToStreamAsync(streamId, eventData, expectedVersion, cancellationToken);
    }

    public async Task<ReadStreamResult<object>> ReadStreamAsync(
        string streamId,
        StreamReadDirection direction = StreamReadDirection.Forward,
        long? fromVersion = null,
        CancellationToken cancellationToken = default)
    {
        // Deserialize then pass events through read pipeline.
        // StoredEventData -> pipeline -> object(s)

        var result = await eventDataStore.ReadStreamAsync(streamId, direction, fromVersion, cancellationToken);

        return result.Status == ReadStreamStatus.Success
            ? ReadStreamResult<object>.Success(GetDeserializedEvents())
            : ReadStreamResult<object>.NotFound();

        async IAsyncEnumerable<object> GetDeserializedEvents()
        {
            await foreach (var eventData in result.Events.WithCancellation(cancellationToken))
            {
                yield return eventSerializer.Deserialize(eventData.EventName, eventData.Payload);
            }
        }
    }
}