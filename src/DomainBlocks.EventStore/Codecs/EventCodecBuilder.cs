using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.EventStore.Codecs;

public sealed class EventCodecBuilder<TEvent, TEventData, TMetadata> where TEvent : notnull where TEventData : notnull
{
    private readonly List<EventTypeMapping> _typeMappings = [];
    private EventTypeMap? _typeMap;
    private IObjectSerializer<TEventData>? _eventSerializer;
    private IMetadataSerializer<TMetadata>? _metadataSerializer;
    private readonly List<IEventContractMapper<TEvent>> _contractMappers = [];

    public EventCodecBuilder<TEvent, TEventData, TMetadata> MapEvents(params EventTypeMapping[] mappings)
    {
        ArgumentNullException.ThrowIfNull(mappings);

        if (_typeMap is not null)
            throw new InvalidOperationException("MapEvents cannot be combined with UseEventTypeMap.");

        _typeMappings.AddRange(mappings);
        return this;
    }

    public EventCodecBuilder<TEvent, TEventData, TMetadata> MapEvent<T>(string? name = null) where T : TEvent
    {
        return MapEvents(EventTypeMapping.ReadWrite<T>(name));
    }

    public EventCodecBuilder<TEvent, TEventData, TMetadata> UseEventTypeMap(EventTypeMap typeMap)
    {
        ArgumentNullException.ThrowIfNull(typeMap);

        if (_typeMappings.Count > 0)
            throw new InvalidOperationException("UseEventTypeMap cannot be combined with MapEvents.");

        _typeMap = typeMap;
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

    public EventCodecBuilder<TEvent, TEventData, TMetadata> AddContractMappers(
        params IEventContractMapper<TEvent>[] mappers)
    {
        ArgumentNullException.ThrowIfNull(mappers);

        _contractMappers.AddRange(mappers);
        return this;
    }

    public IEventCodec<TEvent, TEventData, TMetadata> Build()
    {
        return Build(
            static () => throw new InvalidOperationException(
                "No event serializer is configured. Call UseEventSerializer(...)."),
            static () => throw new InvalidOperationException(
                "No metadata serializer is configured. Call UseMetadataSerializer(...)."));
    }

    public IEventCodec<TEvent, TEventData, TMetadata> Build(
        Func<IObjectSerializer<TEventData>> defaultEventSerializer,
        Func<IMetadataSerializer<TMetadata>> defaultMetadataSerializer)
    {
        ArgumentNullException.ThrowIfNull(defaultEventSerializer);
        ArgumentNullException.ThrowIfNull(defaultMetadataSerializer);

        return EventCodec.Create(new EventCodecOptions<TEvent, TEventData, TMetadata>
        {
            TypeMap = _typeMap ?? BuildTypeMap(),
            EventSerializer = _eventSerializer ?? defaultEventSerializer(),
            MetadataSerializer = _metadataSerializer ?? defaultMetadataSerializer(),
            ContractMappers = _contractMappers,
        });
    }

    private EventTypeMap BuildTypeMap()
    {
        if (_typeMappings.Count == 0)
            throw new InvalidOperationException("No event types are mapped.");

        var mappedTypes = _typeMappings.Select(m => m.EventType).ToHashSet();

        var contractMappings = _contractMappers
            .Select(m => m.ContractType)
            .Distinct()
            .Where(t => !mappedTypes.Contains(t))
            .Select(t => EventTypeMapping.ReadWrite(t));

        return EventTypeMap.Create([.. _typeMappings, .. contractMappings]);
    }
}