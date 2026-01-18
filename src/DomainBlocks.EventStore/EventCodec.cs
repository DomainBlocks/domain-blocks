using System.Collections.Frozen;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.EventStore;

public static class EventCodec
{
    public static EventCodec<TEvent, TEventData, TMetadata> Create<TEvent, TEventData, TMetadata>(
        EventCodecOptions<TEvent, TEventData, TMetadata> options)
        where TEvent : notnull
        where TEventData : notnull
    {
        return new EventCodec<TEvent, TEventData, TMetadata>(options);
    }
}

public sealed class EventCodec<TEvent, TEventData, TMetadata> : IEventCodec<TEvent, TEventData, TMetadata>
    where TEvent : notnull
    where TEventData : notnull
{
    private readonly EventTypeMap _eventTypeMap;
    private readonly FrozenDictionary<Type, IEventContractMapper<TEvent>> _contractMapperByEventType;
    private readonly FrozenDictionary<Type, IEventContractMapper<TEvent>> _contractMappersByContractType;
    private readonly IMetadataContributor<TEvent>[] _metadataContributors;
    private readonly IObjectSerializer<TEventData> _eventSerializer;
    private readonly IMetadataSerializer<TMetadata> _metadataSerializer;

    public EventCodec(EventCodecOptions<TEvent, TEventData, TMetadata> options)
    {
        var contractMapperArray = options.ContractMappers as IEventContractMapper<TEvent>[] ??
                                  options.ContractMappers.ToArray();

        _eventTypeMap = options.TypeMap;
        _contractMapperByEventType = contractMapperArray.ToFrozenDictionary(x => x.EventType);
        _contractMappersByContractType = contractMapperArray.ToFrozenDictionary(x => x.ContractType);
        _metadataContributors = options.MetadataContributors.ToArray();
        _eventSerializer = options.EventSerializer;
        _metadataSerializer = options.MetadataSerializer;
    }

    public IEventEncoder<TEvent, TEventData, TMetadata> CreateEncoder()
    {
        return new EventEncoder<TEvent, TEventData, TMetadata>(
            _eventTypeMap,
            _contractMapperByEventType,
            _metadataContributors,
            _eventSerializer,
            _metadataSerializer);
    }

    public DecodedEvent<TEvent> Decode(string eventName, TEventData eventData, TMetadata? metadata)
    {
        var eventType = _eventTypeMap.Read.GetEventType(eventName);
        var deserializedEvent = _eventSerializer.Deserialize(eventData, eventType);

        if (_contractMappersByContractType.TryGetValue(deserializedEvent.GetType(), out var contractMapper))
            deserializedEvent = contractMapper.FromContract(deserializedEvent);

        var hasMetadata = !EqualityComparer<TMetadata>.Default.Equals(metadata, default);

        var deserializedMetadata = hasMetadata
            ? _metadataSerializer.Deserialize(metadata!)
            : FrozenDictionary<string, string>.Empty;

        return DecodedEvent.Create((TEvent)deserializedEvent, deserializedMetadata);
    }
}