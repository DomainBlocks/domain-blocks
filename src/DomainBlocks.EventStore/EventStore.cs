using System.Collections.Frozen;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.EventStore;

public class EventStore<TPayload> : IEventStore
{
    private readonly IEventStoreBackend<TPayload> _backend;
    private readonly EventTypeMapper _eventTypeMapper;
    private readonly IPayloadSerializer<TPayload> _serializer;
    private readonly FrozenDictionary<Type, IEventContractMapper> _contractMappersByEventType;
    private readonly FrozenDictionary<Type, IEventContractMapper> _contractMappersByContractType;
    private readonly FrozenDictionary<Type, IEventReadTransform> _readTransforms;

    public EventStore(EventStoreOptions<TPayload> options)
    {
        _backend = options.Backend;
        _eventTypeMapper = new EventTypeMapper(options.TypeMappings);
        _serializer = options.Serializer;
        _contractMappersByEventType = options.ContractMappers.ToFrozenDictionary(x => x.EventType);
        _contractMappersByContractType = options.ContractMappers.ToFrozenDictionary(x => x.ContractType);
        _readTransforms = options.ReadTransforms.ToFrozenDictionary(x => x.FromType);
    }

    public async Task AppendToStreamAsync(
        string streamId,
        IEnumerable<object> events,
        ExpectedStreamVersion expectedVersion = default,
        CancellationToken cancellationToken = default)
    {
        var records = events
            .Select(@event =>
            {
                string eventName;

                if (_contractMappersByEventType.TryGetValue(@event.GetType(), out var mapper))
                {
                    eventName = _eventTypeMapper.GetEventName(mapper.ContractType);
                    @event = mapper.ToContract(@event);
                }
                else
                {
                    eventName = _eventTypeMapper.GetEventName(@event.GetType());
                }

                var payload = _serializer.Serialize(@event);
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
        var result = await _backend.ReadStreamAsync(streamId, direction, fromPosition, cancellationToken);

        return result.Status == ReadStreamStatus.Success
            ? ReadStreamResult.Success(TransformEvents())
            : ReadStreamResult.NotFound<EventRecord<object>>();

        async IAsyncEnumerable<EventRecord<object>> TransformEvents()
        {
            var queue = _readTransforms.Count > 0 ? new Queue<object>() : null;

            await foreach (var record in result.Events.WithCancellation(cancellationToken))
            {
                var eventType = _eventTypeMapper.GetEventType(record.Header.EventName);
                var sourceEvent = _serializer.Deserialize(record.Payload, eventType);

                if (_contractMappersByContractType.TryGetValue(sourceEvent.GetType(), out var mapper))
                    sourceEvent = mapper.FromContract(sourceEvent);

                if (queue == null)
                {
                    yield return new EventRecord<object>(record.Header, sourceEvent);
                    continue;
                }

                queue.Enqueue(sourceEvent);

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