using System.Collections.Frozen;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.EventStore;

public static class EventDecoder
{
    public static EventDecoder<TEvent, TEventData, TMetadata> Create<TEvent, TEventData, TMetadata>(
        EventDecoderOptions<TEvent, TEventData, TMetadata> options)
        where TEvent : notnull
        where TEventData : notnull
    {
        return new EventDecoder<TEvent, TEventData, TMetadata>(options);
    }
}

public sealed class EventDecoder<TEvent, TEventData, TMetadata>(
    EventDecoderOptions<TEvent, TEventData, TMetadata> options) :
    IEventDecoder<TEvent, TEventData, TMetadata>
    where TEvent : notnull
    where TEventData : notnull
{
    private readonly ReadEventTypeMap _eventTypeMap = options.TypeMap;
    private readonly IObjectDeserializer<TEventData> _eventDeserializer = options.EventDeserializer;
    private readonly IMetadataDeserializer<TMetadata> _metadataDeserializer = options.MetadataDeserializer;

    private readonly FrozenDictionary<Type, IReadEventContractMapper<TEvent>> _contractMappers =
        options.ContractMappers.ToFrozenDictionary(x => x.ContractType);

    public DecodedEvent<TEvent> Decode(string eventName, TEventData eventData, TMetadata? metadata)
    {
        var eventType = _eventTypeMap.GetEventType(eventName);
        var deserializedEvent = _eventDeserializer.Deserialize(eventData, eventType);

        if (_contractMappers.TryGetValue(deserializedEvent.GetType(), out var contractMapper))
            deserializedEvent = contractMapper.FromContract(deserializedEvent);

        var hasMetadata = !EqualityComparer<TMetadata>.Default.Equals(metadata, default);

        var deserializedMetadata = hasMetadata
            ? _metadataDeserializer.Deserialize(metadata!)
            : FrozenDictionary<string, string>.Empty;

        return DecodedEvent.Create((TEvent)deserializedEvent, deserializedMetadata);
    }
}