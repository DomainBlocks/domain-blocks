using DomainBlocks.Persistence.Abstractions.Events;
using DomainBlocks.Serialization.Events;

namespace DomainBlocks.Persistence.Events;

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