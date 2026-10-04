using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.EventStore.Codecs;

public sealed class EventCodecBuilder<TEvent, TEventData, TMetadata> where TEvent : notnull where TEventData : notnull
{
    private EventTypeMapBuilder? _typeMapBuilder;
    private EventTypeMap? _typeMap;
    private IObjectSerializer<TEventData>? _eventSerializer;
    private IMetadataSerializer<TMetadata>? _metadataSerializer;
    private readonly List<IEventContractMapper<TEvent>> _contractMappers = [];

    public EventCodecBuilder<TEvent, TEventData, TMetadata> MapEvent<T>(string? eventName = null) where T : TEvent
    {
        if (_typeMap is not null)
            throw new InvalidOperationException("MapEvent cannot be combined with UseEventTypeMap.");

        _typeMapBuilder ??= new EventTypeMapBuilder();
        _typeMapBuilder.Add<T>(eventName);
        return this;
    }

    public EventCodecBuilder<TEvent, TEventData, TMetadata> UseEventTypeMap(EventTypeMap typeMap)
    {
        ArgumentNullException.ThrowIfNull(typeMap);

        if (_typeMapBuilder is not null)
            throw new InvalidOperationException("UseEventTypeMap cannot be combined with MapEvent.");

        typeMap.ValidateAssignableTo(typeof(TEvent));

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

        var typeMap = _typeMap ??
                      _typeMapBuilder?.Build() ??
                      throw new InvalidOperationException("No event types are mapped.");

        return EventCodec.Create(new EventCodecOptions<TEvent, TEventData, TMetadata>
        {
            TypeMap = typeMap,
            EventSerializer = _eventSerializer ?? defaultEventSerializer(),
            MetadataSerializer = _metadataSerializer ?? defaultMetadataSerializer(),
            ContractMappers = _contractMappers,
        });
    }
}