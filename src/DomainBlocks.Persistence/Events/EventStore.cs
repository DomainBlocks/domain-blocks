using DomainBlocks.Persistence.Abstractions.Events;
using DomainBlocks.Serialization.Abstractions;
using DomainBlocks.Serialization.Events;

namespace DomainBlocks.Persistence.Events;

public static class EventStore
{
    public static EventStore<TPayload> Create<TPayload>(
        IEventStoreBackend<TPayload> backend,
        IEnumerable<EventTypeMapping> eventTypeMappings,
        ISerializer<TPayload> serializer)
    {
        return new EventStore<TPayload>(backend, eventTypeMappings, serializer);
    }
}

public class EventStore<TPayload>(
    IEventStoreBackend<TPayload> backend,
    IEnumerable<EventTypeMapping> eventTypeMappings,
    ISerializer<TPayload> serializer) :
    IEventStore
{
    private readonly EventTypeMapper _eventTypeMapper = new(eventTypeMappings);

    public async Task AppendToStreamAsync(
        string streamId,
        IEnumerable<object> events,
        ExpectedStreamVersion? expectedVersion = null,
        CancellationToken cancellationToken = default)
    {
        // Pass events through write pipeline first. End of the line is serialisation.
        // Object -> pipeline -> EventData

        var records = events
            .Select(x =>
            {
                var eventName = _eventTypeMapper.GetEventName(x);
                var payload = serializer.Serialize(x);
                return new NewEventRecord<TPayload>(new NewEventHeader(eventName), payload);
            });

        await backend.AppendToStreamAsync(streamId, records, expectedVersion, cancellationToken);
    }

    public async Task<ReadStreamResult<EventRecord<object>>> ReadStreamAsync(
        string streamId,
        StreamReadDirection direction = StreamReadDirection.Forward,
        StreamPosition? fromPosition = null,
        CancellationToken cancellationToken = default)
    {
        // Deserialize then pass events through read pipeline.
        // StoredEventData -> pipeline -> object(s)

        var result = await backend.ReadStreamAsync(streamId, direction, fromPosition, cancellationToken);

        return result.Status == ReadStreamStatus.Success
            ? ReadStreamResult<EventRecord<object>>.Success(GetDeserializedEvents())
            : ReadStreamResult<EventRecord<object>>.NotFound();

        async IAsyncEnumerable<EventRecord<object>> GetDeserializedEvents()
        {
            await foreach (var record in result.Events.WithCancellation(cancellationToken))
            {
                var eventType = _eventTypeMapper.GetEventType(record.Header.EventName);
                var deserializedEvent = serializer.Deserialize(record.Payload, eventType);

                if (deserializedEvent == null)
                    throw new Exception("TODO (DS): Deserialize event is null.");

                yield return new EventRecord<object>(record.Header, deserializedEvent);
            }
        }
    }
}