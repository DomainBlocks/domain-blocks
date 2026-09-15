using System.Collections.Frozen;
using DomainBlocks.EventStore.Abstractions.Codecs;
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

/// <summary>
/// The default codec: resolves stored names through an <see cref="EventTypeMap"/>, maps domain events to and from
/// their wire contracts where a mapper is registered, and serializes event data and metadata. It holds no state per
/// call and is safe to share.
/// </summary>
public sealed class EventCodec<TEvent, TEventData, TMetadata> : IEventCodec<TEvent, TEventData, TMetadata>
    where TEvent : notnull
    where TEventData : notnull
{
    private readonly EventTypeMap _typeMap;
    private readonly IObjectSerializer<TEventData> _eventSerializer;
    private readonly IMetadataSerializer<TMetadata> _metadataSerializer;
    private readonly FrozenDictionary<Type, IEventContractMapper<TEvent>> _appendMappers;
    private readonly FrozenDictionary<Type, IEventContractMapper<TEvent>> _readMappers;

    public EventCodec(EventCodecOptions<TEvent, TEventData, TMetadata> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _typeMap = options.TypeMap;
        _eventSerializer = options.EventSerializer;
        _metadataSerializer = options.MetadataSerializer;

        var mappers = options.ContractMappers.ToArray();
        _appendMappers = mappers.ToFrozenDictionary(x => x.EventType, x => x);
        _readMappers = mappers.ToFrozenDictionary(x => x.ContractType, x => x);
    }

    public EncodedEvent<TEventData, TMetadata> Encode(
        TEvent payload,
        ReadOnlySpan<KeyValuePair<string, string>> metadata)
    {
        var wireObject = _appendMappers.TryGetValue(payload.GetType(), out var mapper)
            ? mapper.ToContract(payload)
            : payload;

        var eventName = _typeMap.GetEventName(wireObject.GetType());
        var eventData = _eventSerializer.Serialize(wireObject);

        var serializedMetadata = metadata.Length > 0
            ? _metadataSerializer.Serialize(metadata)
            : default;

        return EncodedEvent.Create(eventName, eventData, serializedMetadata);
    }

    public DecodedEvent<TEvent> Decode(string eventName, TEventData eventData, TMetadata? metadata)
    {
        var wireType = _typeMap.GetEventType(eventName);
        var wireObject = _eventSerializer.Deserialize(eventData, wireType);

        var payload = _readMappers.TryGetValue(wireObject.GetType(), out var mapper)
            ? mapper.FromContract(wireObject)
            : (TEvent)wireObject;

        var deserializedMetadata = !EqualityComparer<TMetadata>.Default.Equals(metadata, default)
            ? _metadataSerializer.Deserialize(metadata!)
            : FrozenDictionary<string, string>.Empty;

        return DecodedEvent.Create(payload, deserializedMetadata);
    }
}