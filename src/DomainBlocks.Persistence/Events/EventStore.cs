using DomainBlocks.Persistence.Abstractions.Events;
using DomainBlocks.Serialization.Events;

namespace DomainBlocks.Persistence.Events;

public class EventStore<TEventBase, TPayload>(
    IEventDataStore<TPayload> eventDataStore,
    EventSerializer<TPayload> eventSerializer) : IEventStore<TEventBase> where TEventBase : notnull
{
    public async Task AppendToStreamAsync(
        string streamId,
        IEnumerable<TEventBase> events,
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

    public async Task<ReadStreamResult<TEventBase>> ReadStreamAsync(
        string streamId,
        StreamReadDirection direction = StreamReadDirection.Forward,
        long? fromVersion = null,
        CancellationToken cancellationToken = default)
    {
        var result = await eventDataStore.ReadStreamAsync(streamId, direction, fromVersion, cancellationToken);

        return result.Status == ReadStreamStatus.Success
            ? ReadStreamResult<TEventBase>.Success(GetDeserializedEvents())
            : ReadStreamResult<TEventBase>.NotFound();

        async IAsyncEnumerable<TEventBase> GetDeserializedEvents()
        {
            await foreach (var eventData in result.Events.WithCancellation(cancellationToken))
            {
                yield return (TEventBase)eventSerializer.Deserialize(eventData.EventName, eventData.Payload);
            }
        }
    }
}