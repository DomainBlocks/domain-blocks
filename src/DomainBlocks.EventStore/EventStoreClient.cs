using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.EventStore;

public class EventStoreClient<TPayload> : IEventStoreClient where TPayload : notnull
{
    private readonly IEventStoreAdapter<TPayload> _adapter;
    private readonly EventTypeMap _eventTypeMap;
    private readonly IPayloadSerializer<TPayload> _serializer;
    private readonly FrozenDictionary<Type, IEventContractMapper> _contractMappersByEventType;
    private readonly FrozenDictionary<Type, IEventContractMapper> _contractMappersByContractType;
    private readonly FrozenDictionary<Type, IEventReadTransform> _readTransforms;

    public EventStoreClient(EventStoreClientOptions<TPayload> options)
    {
        _adapter = options.Adapter;
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

        await _adapter.AppendToStreamAsync(streamId, serializedEvents, expectedState, cancellationToken);
    }

    public async IAsyncEnumerable<CommittedEvent<object>> ReadStreamAsync(
        string streamId,
        ReadStreamOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var serializedEvents = _adapter.ReadStreamAsync(streamId, options, cancellationToken);
        var queue = _readTransforms.Count > 0 ? new Queue<object>() : null;

        await foreach (var serializedEvent in serializedEvents)
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