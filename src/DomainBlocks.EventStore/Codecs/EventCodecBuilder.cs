using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.EventStore.Codecs;

/// <summary>
/// Assembles an <see cref="IEventCodec{TEvent, TEventData, TMetadata}"/> from event type mappings, contract mappers
/// and serializers. Store builders own one of these and forward to it, so callers normally see its methods on the
/// store builder; it is public so that a codec can also be built on its own.
/// </summary>
/// <typeparam name="TEvent">The event base type.</typeparam>
/// <typeparam name="TEventData">The store's representation of event data.</typeparam>
/// <typeparam name="TMetadata">The store's representation of metadata.</typeparam>
public sealed class EventCodecBuilder<TEvent, TEventData, TMetadata>
    where TEvent : notnull
    where TEventData : notnull
{
    private readonly List<EventTypeMapping> _mappings = [];
    private readonly List<IEventContractMapper<TEvent>> _contractMappers = [];
    private EventTypeMap? _typeMap;
    private IObjectSerializer<TEventData>? _eventSerializer;
    private IMetadataSerializer<TMetadata>? _metadataSerializer;
    private IEventCodec<TEvent, TEventData, TMetadata>? _codec;

    /// <summary>
    /// Adds mappings between event CLR types and their stored names. May be called repeatedly; the map is built once
    /// at <see cref="Build()"/>. Not combinable with <see cref="UseEventTypeMap"/>.
    /// </summary>
    public EventCodecBuilder<TEvent, TEventData, TMetadata> MapEvents(params EventTypeMapping[] mappings)
    {
        ArgumentNullException.ThrowIfNull(mappings);

        if (_typeMap is not null)
            throw new InvalidOperationException("MapEvents cannot be combined with UseEventTypeMap.");

        _mappings.AddRange(mappings);
        return this;
    }

    /// <summary>
    /// Maps <typeparamref name="T"/> for reading and writing under <paramref name="name"/>, or its type name.
    /// </summary>
    public EventCodecBuilder<TEvent, TEventData, TMetadata> MapEvent<T>(string? name = null) where T : TEvent
    {
        return MapEvents(EventTypeMapping.ReadWrite<T>(name));
    }

    /// <summary>
    /// Uses a prebuilt type map instead of accumulating mappings. Contract types are not registered automatically on
    /// this path; the map must already name them. Not combinable with <see cref="MapEvents"/>.
    /// </summary>
    public EventCodecBuilder<TEvent, TEventData, TMetadata> UseEventTypeMap(EventTypeMap typeMap)
    {
        ArgumentNullException.ThrowIfNull(typeMap);

        if (_mappings.Count > 0)
            throw new InvalidOperationException("UseEventTypeMap cannot be combined with MapEvents.");

        _typeMap = typeMap;
        return this;
    }

    /// <summary>
    /// Adds mappers between domain events and the contract types that are actually serialized. Each contract type is
    /// mapped for reading and writing under its type name unless a mapping for it was given explicitly, because the
    /// stored name comes from the contract type.
    /// </summary>
    public EventCodecBuilder<TEvent, TEventData, TMetadata> AddContractMappers(
        params IEventContractMapper<TEvent>[] mappers)
    {
        ArgumentNullException.ThrowIfNull(mappers);

        _contractMappers.AddRange(mappers);
        return this;
    }

    public EventCodecBuilder<TEvent, TEventData, TMetadata> UseEventSerializer(IObjectSerializer<TEventData> serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);

        _eventSerializer = serializer;
        return this;
    }

    public EventCodecBuilder<TEvent, TEventData, TMetadata> UseMetadataSerializer(
        IMetadataSerializer<TMetadata> serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);

        _metadataSerializer = serializer;
        return this;
    }

    /// <summary>
    /// Uses a ready-made codec, in place of a type map, contract mappers and serializers.
    /// </summary>
    public EventCodecBuilder<TEvent, TEventData, TMetadata> UseCodec(IEventCodec<TEvent, TEventData, TMetadata> codec)
    {
        ArgumentNullException.ThrowIfNull(codec);

        _codec = codec;
        return this;
    }

    /// <summary>
    /// Builds the codec. Both serializers must have been given.
    /// </summary>
    public IEventCodec<TEvent, TEventData, TMetadata> Build()
    {
        return Build(
            static () => throw new InvalidOperationException(
                "No event serializer is configured. Call UseEventSerializer(...)."),
            static () => throw new InvalidOperationException(
                "No metadata serializer is configured. Call UseMetadataSerializer(...)."));
    }

    /// <summary>
    /// Builds the codec, using the given factories for any serializer that was not configured explicitly. Store
    /// builders pass their format defaults here.
    /// </summary>
    public IEventCodec<TEvent, TEventData, TMetadata> Build(
        Func<IObjectSerializer<TEventData>> defaultEventSerializer,
        Func<IMetadataSerializer<TMetadata>> defaultMetadataSerializer)
    {
        ArgumentNullException.ThrowIfNull(defaultEventSerializer);
        ArgumentNullException.ThrowIfNull(defaultMetadataSerializer);

        if (_codec is not null)
        {
            if (_mappings.Count > 0 || _typeMap is not null || _contractMappers.Count > 0 ||
                _eventSerializer is not null || _metadataSerializer is not null)
            {
                throw new InvalidOperationException(
                    "UseCodec replaces the type map, contract mappers and serializers; do not combine it with them.");
            }

            return _codec;
        }

        var typeMap = _typeMap ?? BuildTypeMap();

        return EventCodec.Create(new EventCodecOptions<TEvent, TEventData, TMetadata>
        {
            TypeMap = typeMap,
            EventSerializer = _eventSerializer ?? defaultEventSerializer(),
            MetadataSerializer = _metadataSerializer ?? defaultMetadataSerializer(),
            ContractMappers = _contractMappers
        });
    }

    private EventTypeMap BuildTypeMap()
    {
        if (_mappings.Count == 0)
        {
            throw new InvalidOperationException(
                "No event types are mapped. Call MapEvents(...), MapEvent<T>(), UseEventTypeMap(...) or UseCodec(...).");
        }

        var mappedTypes = _mappings.Select(m => m.EventType).ToHashSet();

        var contractMappings = _contractMappers
            .Select(m => m.ContractType)
            .Distinct()
            .Where(t => !mappedTypes.Contains(t))
            .Select(t => EventTypeMapping.ReadWrite(t));

        return EventTypeMap.Create([.. _mappings, .. contractMappings]);
    }
}