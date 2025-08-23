using System.Collections.Frozen;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.EventStore;

public class EventStore<TPayload>(EventStoreOptions<TPayload> options) : IEventStore
{
    private readonly IEventStoreBackend<TPayload> _backend = options.Backend;
    private readonly EventTypeMapper _eventTypeMapper = new(options.TypeMappings);
    private readonly ISerializer<TPayload> _serializer = options.Serializer;

    private readonly FrozenDictionary<Type, IEventReadTransform> _readTransforms =
        options.ReadTransforms?.ToFrozenDictionary(x => x.FromType) ??
        FrozenDictionary<Type, IEventReadTransform>.Empty;

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
                var payload = _serializer.Serialize(x);
                return new NewEventRecord<TPayload>(new NewEventHeader(eventName), payload);
            });

        await _backend.AppendToStreamAsync(streamId, records, expectedVersion, cancellationToken);
    }

    public async Task<ReadStreamResult<EventRecord<object>>> ReadStreamAsync(
        string streamId,
        StreamReadDirection direction = StreamReadDirection.Forward,
        StreamPosition? fromPosition = null,
        CancellationToken cancellationToken = default)
    {
        // Deserialize then pass events through read pipeline.
        // StoredEventData -> pipeline -> object(s)

        var result = await _backend.ReadStreamAsync(streamId, direction, fromPosition, cancellationToken);

        return result.Status == ReadStreamStatus.Success
            ? ReadStreamResult<EventRecord<object>>.Success(TransformEvents())
            : ReadStreamResult<EventRecord<object>>.NotFound();

        async IAsyncEnumerable<EventRecord<object>> TransformEvents()
        {
            var queue = _readTransforms.Count > 0 ? new Queue<object>() : null;

            await foreach (var record in result.Events.WithCancellation(cancellationToken))
            {
                var eventType = _eventTypeMapper.GetEventType(record.Header.EventName);
                var deserializedEvent = _serializer.Deserialize(record.Payload, eventType);

                if (deserializedEvent == null)
                    throw new Exception("TODO (DS): Deserialize event is null.");

                if (queue == null)
                {
                    yield return new EventRecord<object>(record.Header, deserializedEvent);
                    continue;
                }

                queue.Enqueue(deserializedEvent);

                while (queue.TryDequeue(out var @event))
                {
                    if (_readTransforms.TryGetValue(@event.GetType(), out var transform))
                    {
                        var transformedEvents = transform.Apply(@event, record.Header);

                        foreach (var transformedEvent in transformedEvents)
                            queue.Enqueue(transformedEvent);
                    }
                    else
                    {
                        yield return new EventRecord<object>(record.Header, @event);
                    }
                }
            }
        }
    }
}