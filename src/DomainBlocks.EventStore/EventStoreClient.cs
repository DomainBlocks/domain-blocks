using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Abstractions.Events;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.EventStore;

public sealed class EventStoreClient<TEventBase, TEventData, TMetadata> :
    IEventStoreClient<TEventBase>
    where TEventBase : class
    where TEventData : notnull
    where TMetadata : notnull
{
    private readonly IEventStoreConnectionProvider<TEventData, TMetadata> _connectionProvider;
    private readonly EventTypeMap _eventTypeMap;
    private readonly IObjectSerializer<TEventData> _eventSerializer;
    private readonly IMetadataSerializer<TMetadata> _metadataSerializer;
    private readonly IMetadataContributor<TEventBase>[] _metadataContributors;
    private readonly FrozenDictionary<Type, IEventContractMapper<TEventBase>> _contractMappersByEventType;
    private readonly FrozenDictionary<Type, IEventContractMapper<TEventBase>> _contractMappersByContractType;

    public EventStoreClient(EventStoreClientOptions<TEventBase, TEventData, TMetadata> options)
    {
        _connectionProvider = options.ConnectionProvider;
        _eventTypeMap = options.TypeMap;
        _eventSerializer = options.EventSerializer;
        _metadataSerializer = options.MetadataSerializer;
        _metadataContributors = options.MetadataContributors.ToArray();
        _contractMappersByEventType = options.ContractMappers.ToFrozenDictionary(x => x.EventType);
        _contractMappersByContractType = options.ContractMappers.ToFrozenDictionary(x => x.ContractType);
    }

    public async Task AppendToStreamAsync(
        string streamId,
        IEnumerable<AppendEvent<TEventBase>> events,
        AppendToStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await using var scope = await _connectionProvider.AcquireAsync(cancellationToken).ConfigureAwait(false);
        var serializedEvents = SerializeEvents(events);

        await scope.Connection
            .AppendToStreamAsync(streamId, serializedEvents, options, cancellationToken)
            .ConfigureAwait(false);
    }

    public async IAsyncEnumerable<ReadEvent<TEventBase>> ReadStreamAsync(
        string streamId,
        ReadStreamOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var scope = await _connectionProvider.AcquireAsync(cancellationToken).ConfigureAwait(false);
        var serializedEvents = scope.Connection.ReadStreamAsync(streamId, options, cancellationToken);

        await foreach (var serializedEvent in serializedEvents.ConfigureAwait(false))
        {
            var header = serializedEvent.Header;
            var eventType = _eventTypeMap.GetEventType(header.EventName);
            var deserializedValue = _eventSerializer.Deserialize(serializedEvent.Value, eventType);

            if (_contractMappersByContractType.TryGetValue(deserializedValue.GetType(), out var mapper))
                deserializedValue = mapper.FromContract(deserializedValue);

            yield return ReadEvent.Create(header, (TEventBase)deserializedValue);
        }
    }

    private IEnumerable<AppendEvent<TEventData, TMetadata>> SerializeEvents(IEnumerable<AppendEvent<TEventBase>> events)
    {
        var metadata = new Dictionary<string, string>();
        var metadataWriter = new MetadataWriter(metadata);

        foreach (var e in events)
        {
            metadata.Clear();

            var @event = e.Event;
            object? contract = null;
            string eventName;

            if (_contractMappersByEventType.TryGetValue(@event.GetType(), out var mapper))
            {
                contract = mapper.ToContract(@event);
                eventName = _eventTypeMap.GetEventName(mapper.ContractType);
            }
            else
            {
                eventName = _eventTypeMap.GetEventName(@event.GetType());
            }

            var serializedEvent = _eventSerializer.Serialize(contract ?? @event);

            foreach (var metadataContributor in _metadataContributors)
                metadataContributor.Contribute(@event, contract, eventName, metadataWriter);

            foreach (var (key, value) in e.Metadata)
                metadata[key] = value;

            var serializedMetadata = metadata.Count > 0 ? _metadataSerializer.Serialize(metadata) : default;

            yield return Abstractions.Events.AppendEvent.Create(eventName, serializedEvent, serializedMetadata);
        }
    }
}