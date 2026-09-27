using System.Collections.Frozen;
using System.Reflection;
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

    public IReadOnlyCollection<string> ResolveEventNames(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);

        // A name is mapped to the type that is deserialized, which a contract mapper then turns into the event.
        return
        [
            .. from read in _typeMap.EventTypesByName
            let decodedType = _readContractMappers.TryGetValue(read.Value, out var mapper)
                ? mapper.EventType
                : read.Value
            where eventType.IsAssignableFrom(decodedType)
            select read.Key
        ];
    }

    public string? ResolveStoredPath(Type eventType, IReadOnlyList<MemberInfo> members)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        ArgumentNullException.ThrowIfNull(members);

        // Only of a type that is stored as itself, under every name that is read as it. A contract is stored in its
        // own shape, and what a member of a base type is stored under is up to each type that derives from it.
        var storedTypes = _typeMap.EventTypesByName.Values
            .Where(x => eventType.IsAssignableFrom(_readContractMappers.GetValueOrDefault(x)?.EventType ?? x))
            .Distinct()
            .ToArray();

        if (members.Count == 0 || storedTypes is not [var storedType] || storedType != eventType)
            return null;

        var names = new List<string>(members.Count);
        var type = eventType;

        foreach (var member in members)
        {
            // A dot in a name would make two names of it.
            if (_eventSerializer.GetStoredName(type, member) is not { Length: > 0 } name || name.Contains('.'))
                return null;

            names.Add(name);

            type = member switch
            {
                PropertyInfo property => property.PropertyType,
                FieldInfo field => field.FieldType,
                _ => null
            };

            if (type is null)
                return null;
        }

        return string.Join('.', names);
    }

    private IReadOnlyDictionary<string, string> DecodeMetadata(TMetadata? metadata)
    {
        return !EqualityComparer<TMetadata>.Default.Equals(metadata, default)
            ? _metadataSerializer.Deserialize(metadata!)
            : FrozenDictionary<string, string>.Empty;
    }
}