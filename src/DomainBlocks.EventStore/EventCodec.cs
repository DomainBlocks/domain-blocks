using System.Collections.Frozen;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.EventStore;

public static class EventCodec
{
    public static EventCodec<TEventBase, TEventData, TMetadata> Create<TEventBase, TEventData, TMetadata>(
        EventCodecOptions<TEventBase, TEventData, TMetadata> options)
        where TEventBase : class
        where TEventData : notnull
        where TMetadata : notnull
    {
        return new EventCodec<TEventBase, TEventData, TMetadata>(options);
    }
}

public sealed class EventCodec<TEventBase, TEventData, TMetadata> :
    IEventCodec<TEventBase, TEventData, TMetadata>
    where TEventBase : class
    where TEventData : notnull
    where TMetadata : notnull
{
    private readonly EventTypeMap _eventTypeMap;
    private readonly FrozenDictionary<Type, IEventContractMapper<TEventBase>> _contractMapperByEventType;
    private readonly FrozenDictionary<Type, IEventContractMapper<TEventBase>> _contractMappersByContractType;
    private readonly IMetadataContributor<TEventBase>[] _metadataContributors;
    private readonly IObjectSerializer<TEventData> _eventSerializer;
    private readonly IMetadataSerializer<TMetadata> _metadataSerializer;

    public EventCodec(EventCodecOptions<TEventBase, TEventData, TMetadata> options)
    {
        var contractMapperArray = options.ContractMappers as IEventContractMapper<TEventBase>[] ??
                                  options.ContractMappers.ToArray();

        _eventTypeMap = options.TypeMap;
        _contractMapperByEventType = contractMapperArray.ToFrozenDictionary(x => x.EventType);
        _contractMappersByContractType = contractMapperArray.ToFrozenDictionary(x => x.ContractType);
        _metadataContributors = options.MetadataContributors.ToArray();
        _eventSerializer = options.EventSerializer;
        _metadataSerializer = options.MetadataSerializer;
    }

    public IEventEncoder<TEventBase, TEventData, TMetadata> CreateEncoder()
    {
        return new EventEncoder<TEventBase, TEventData, TMetadata>(
            _eventTypeMap,
            _contractMapperByEventType,
            _metadataContributors,
            _eventSerializer,
            _metadataSerializer);
    }

    public DecodedEvent<TEventBase> Decode(string eventName, TEventData eventData, TMetadata? metadata)
    {
        var eventType = _eventTypeMap.GetEventType(eventName);
        var deserializedEvent = _eventSerializer.Deserialize(eventData, eventType);

        if (_contractMappersByContractType.TryGetValue(deserializedEvent.GetType(), out var contractMapper))
            deserializedEvent = contractMapper.FromContract(deserializedEvent);

        var hasMetadata = !EqualityComparer<TMetadata>.Default.Equals(metadata, default);

        var deserializedMetadata = hasMetadata
            ? _metadataSerializer.Deserialize(metadata!)
            : FrozenDictionary<string, string>.Empty;

        return DecodedEvent.Create((TEventBase)deserializedEvent, deserializedMetadata);
    }
}