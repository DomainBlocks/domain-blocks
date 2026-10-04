using System.Collections.Frozen;
using System.Diagnostics;
using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.EventStore.Codecs;

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
    private readonly EventTypeMap _typeMap;
    private readonly IObjectSerializer<TEventData> _eventSerializer;
    private readonly IMetadataSerializer<TMetadata> _metadataSerializer;
    private readonly FrozenDictionary<Type, IEventContractMapper<TEvent>> _contractMappers;

    public EventCodec(EventCodecOptions<TEvent, TEventData, TMetadata> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.TypeMap.ValidateAssignableTo(typeof(TEvent));

        _typeMap = options.TypeMap;
        _eventSerializer = options.EventSerializer;
        _metadataSerializer = options.MetadataSerializer;
        _contractMappers = options.ContractMappers.ToFrozenDictionary(x => x.EventType, x => x);
    }

    public EncodedEvent<TEventData, TMetadata> Encode(
        TEvent payload,
        ReadOnlySpan<KeyValuePair<string, string>> metadata)
    {
        var eventName = _typeMap.GetEventName(payload.GetType());

        var payloadToSerialize = _contractMappers.TryGetValue(payload.GetType(), out var mapper)
            ? mapper.ToContract(payload)
            : payload;

        var eventData = _eventSerializer.Serialize(payloadToSerialize);

        var serializedMetadata = metadata.Length > 0
            ? _metadataSerializer.Serialize(metadata)
            : default;

        return EncodedEvent.Create(eventName, eventData, serializedMetadata);
    }

    public DecodedEvent<TEvent> Decode(string eventName, TEventData eventData, TMetadata? metadata)
    {
        var payload = _typeMap.GetReadMapping(eventName) switch
        {
            EventTypeMapping.ReadToType m => DeserializePayload(m.EventType, eventData),
            EventTypeMapping.ReadToInstance m => (TEvent)m.Instance,
            var m => throw new UnreachableException($"Unknown read mapping type '{m.GetType()}'.")
        };

        return DecodedEvent.Create(payload, DeserializeMetadata(metadata));
    }

    private TEvent DeserializePayload(Type eventType, TEventData eventData)
    {
        if (_contractMappers.TryGetValue(eventType, out var mapper))
            return mapper.FromContract(_eventSerializer.Deserialize(eventData, mapper.ContractType));

        return (TEvent)_eventSerializer.Deserialize(eventData, eventType);
    }

    private IReadOnlyDictionary<string, string> DeserializeMetadata(TMetadata? metadata)
    {
        return !EqualityComparer<TMetadata>.Default.Equals(metadata, default)
            ? _metadataSerializer.Deserialize(metadata!)
            : FrozenDictionary<string, string>.Empty;
    }
}