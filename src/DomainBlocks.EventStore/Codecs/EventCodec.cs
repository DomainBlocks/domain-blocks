using System.Collections.Frozen;
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
    private readonly FrozenDictionary<Type, IEventContractMapper<TEvent>> _writeContractMappers;
    private readonly FrozenDictionary<Type, IEventContractMapper<TEvent>> _readContractMappers;

    public EventCodec(EventCodecOptions<TEvent, TEventData, TMetadata> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _typeMap = options.TypeMap;
        _eventSerializer = options.EventSerializer;
        _metadataSerializer = options.MetadataSerializer;

        var mappers = options.ContractMappers.ToArray();
        _writeContractMappers = mappers.ToFrozenDictionary(x => x.EventType, x => x);
        _readContractMappers = mappers.ToFrozenDictionary(x => x.ContractType, x => x);
    }

    public EncodedEvent<TEventData, TMetadata> Encode(
        TEvent payload,
        ReadOnlySpan<KeyValuePair<string, string>> metadata)
    {
        var payloadToSerialize = _writeContractMappers.TryGetValue(payload.GetType(), out var mapper)
            ? mapper.ToContract(payload)
            : payload;

        var eventName = _typeMap.GetEventName(payloadToSerialize.GetType());
        var eventData = _eventSerializer.Serialize(payloadToSerialize);

        var serializedMetadata = metadata.Length > 0
            ? _metadataSerializer.Serialize(metadata)
            : default;

        return EncodedEvent.Create(eventName, eventData, serializedMetadata);
    }

    public DecodedEvent<TEvent> Decode(string eventName, TEventData eventData, TMetadata? metadata)
    {
        var payloadType = _typeMap.GetEventType(eventName);
        var deserializedPayload = _eventSerializer.Deserialize(eventData, payloadType);

        var payload = _readContractMappers.TryGetValue(deserializedPayload.GetType(), out var mapper)
            ? mapper.FromContract(deserializedPayload)
            : (TEvent)deserializedPayload;

        return DecodedEvent.Create(payload, DecodeMetadata(metadata));
    }

    private IReadOnlyDictionary<string, string> DecodeMetadata(TMetadata? metadata)
    {
        return !EqualityComparer<TMetadata>.Default.Equals(metadata, default)
            ? _metadataSerializer.Deserialize(metadata!)
            : FrozenDictionary<string, string>.Empty;
    }
}