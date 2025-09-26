using System.Collections.Frozen;
using System.Diagnostics;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.EventStore;

public class EventStore<TPayload> : IEventStore where TPayload : notnull
{
    private readonly IEventStoreBackend<TPayload> _backend;
    private readonly EventTypeMap _eventTypeMap;
    private readonly IPayloadSerializer<TPayload> _serializer;
    private readonly FrozenDictionary<Type, IEventContractMapper> _contractMappersByEventType;
    private readonly FrozenDictionary<Type, IEventContractMapper> _contractMappersByContractType;
    private readonly FrozenDictionary<Type, IEventReadTransform> _readTransforms;

    public EventStore(EventStoreOptions<TPayload> options)
    {
        _backend = options.Backend;
        _eventTypeMap = options.TypeMap;
        _serializer = options.Serializer;
        _contractMappersByEventType = options.ContractMappers.ToFrozenDictionary(x => x.EventType);
        _contractMappersByContractType = options.ContractMappers.ToFrozenDictionary(x => x.ContractType);
        _readTransforms = options.ReadTransforms.ToFrozenDictionary(x => x.FromType);
    }

    public Task AppendToStreamAsync(
        string streamId,
        IEnumerable<object> events,
        ExpectedStreamState expectedState = default,
        CancellationToken cancellationToken = default)
    {
        return AppendToStreamAsync(streamId, events.Select(UncommittedEvent.Create), expectedState, cancellationToken);
    }

    public async Task AppendToStreamAsync(
        string streamId,
        IEnumerable<UncommittedEvent<object>> events,
        ExpectedStreamState expectedState = default,
        CancellationToken cancellationToken = default)
    {
        var serializedEvents = events
            .Select(e =>
            {
                string eventName;
                var payload = e.Payload;

                if (_contractMappersByEventType.TryGetValue(payload.GetType(), out var mapper))
                {
                    eventName = _eventTypeMap.GetEventName(mapper.ContractType);
                    payload = mapper.ToContract(payload);
                }
                else
                {
                    eventName = _eventTypeMap.GetEventName(payload.GetType());
                }

                // PoC for adding metadata.
                var header = e.Header
                    .WithEventName(eventName)
                    .WithMetadata("EventClrType", payload.GetType().Name);

                var serializedPayload = _serializer.Serialize(payload);

                return UncommittedEvent.Create(header, serializedPayload);
            });

        await _backend.AppendToStreamAsync(streamId, serializedEvents, expectedState, cancellationToken);
    }

    public async Task<ReadStreamResult<CommittedEvent<object>>> ReadStreamAsync(
        string streamId,
        ReadStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _backend.ReadStreamAsync(streamId, options, cancellationToken);

        return result.Status switch
        {
            ReadStreamStatus.Success => ReadStreamResult.Success(TransformEvents()),
            ReadStreamStatus.StreamNotFound => ReadStreamResult.NotFound<CommittedEvent<object>>(),
            _ => throw new UnreachableException($"Unexpected {nameof(ReadStreamStatus)}: {result.Status}")
        };

        async IAsyncEnumerable<CommittedEvent<object>> TransformEvents()
        {
            var queue = _readTransforms.Count > 0 ? new Queue<object>() : null;

            await foreach (var serializedEvent in result.Events.WithCancellation(cancellationToken))
            {
                var header = serializedEvent.Header;
                var eventType = _eventTypeMap.GetEventType(header.EventName);
                var deserializedPayload = _serializer.Deserialize(serializedEvent.Payload, eventType);

                if (_contractMappersByContractType.TryGetValue(deserializedPayload.GetType(), out var mapper))
                    deserializedPayload = mapper.FromContract(deserializedPayload);

                if (queue == null)
                {
                    yield return CommittedEvent.Create(header, deserializedPayload);
                    continue;
                }

                queue.Enqueue(deserializedPayload);

                while (queue.TryDequeue(out var @event))
                {
                    if (_readTransforms.TryGetValue(@event.GetType(), out var transform))
                    {
                        var transformedEvents = transform.Apply(@event, header);

                        foreach (var transformedEvent in transformedEvents)
                            queue.Enqueue(transformedEvent);
                    }
                    else
                    {
                        yield return CommittedEvent.Create(header, @event);
                    }
                }
            }
        }
    }
}