using DomainBlocks.Experimental.EventSourcing.Persistence.Extensions;
using DomainBlocks.Experimental.EventSourcing.Persistence.Serialization;

namespace DomainBlocks.Experimental.EventSourcing.Persistence.Configuration;

public class EntityStreamConfigBuilder<TRawData>
{
    private readonly Type _entityType;

    public EntityStreamConfigBuilder(Type entityType)
    {
        _entityType = entityType;

        // Use JSON serialization by default for known raw data types.
        if (typeof(TRawData) == typeof(ReadOnlyMemory<byte>))
        {
            EventDataSerializer = (IEventDataSerializer<TRawData>)new JsonBytesEventDataSerializer();
        }
        else if (typeof(TRawData) == typeof(string))
        {
            EventDataSerializer = (IEventDataSerializer<TRawData>)new JsonStringEventDataSerializer();
        }
    }

    private IEventDataSerializer<TRawData>? EventDataSerializer { get; set; }

    public EntityStreamConfigBuilder<TRawData> SetEventDataSerializer(
        IEventDataSerializer<TRawData> eventDataSerializer)
    {
        EventDataSerializer = eventDataSerializer;
        return this;
    }

    public EntityStreamConfig<TRawData> Build()
    {
        if (EventDataSerializer == null)
        {
            throw new InvalidOperationException("Event data serializer not specified.");
        }

        return new EntityStreamConfig<TRawData>(_entityType, EventDataSerializer);
    }
}

public class EntityStreamConfigBuilder
{
    private readonly Type _entityType;
    private readonly List<IEventTypeMappingBuilder> _eventTypeMappingBuilders = new();
    private int? _snapshotEventCount;
    private string _streamIdPrefix;

    public EntityStreamConfigBuilder(Type entityType)
    {
        _entityType = entityType;
        _streamIdPrefix = DefaultStreamIdPrefix.CreateFor(entityType);
    }

    public EventBaseTypeMappingBuilder<TEventBase> MapEventsOfType<TEventBase>()
    {
        var builder = new EventBaseTypeMappingBuilder<TEventBase>();
        _eventTypeMappingBuilders.Add(builder);
        return builder;
    }

    public SingleEventTypeMappingBuilder<TEvent> MapEventType<TEvent>()
    {
        var builder = new SingleEventTypeMappingBuilder<TEvent>();
        _eventTypeMappingBuilders.Add(builder);
        return builder;
    }

    public EntityStreamConfigBuilder SetSnapshotEventCount(int? eventCount)
    {
        _snapshotEventCount = eventCount;
        return this;
    }

    public EntityStreamConfigBuilder SetStreamIdPrefix(string streamIdPrefix)
    {
        if (string.IsNullOrWhiteSpace(streamIdPrefix))
            throw new ArgumentException("Stream ID prefix cannot be null or whitespace.", nameof(streamIdPrefix));

        _streamIdPrefix = streamIdPrefix;
        return this;
    }

    public EntityStreamConfig Build(IEnumerable<EventTypeMapping>? eventTypeMappings = null)
    {
        var eventTypeMap = (eventTypeMappings ?? Enumerable.Empty<EventTypeMapping>())
            .AddOrReplaceWith(_eventTypeMappingBuilders.BuildEventTypeMap())
            .ToEventTypeMap();

        return new EntityStreamConfig(_entityType, eventTypeMap, _snapshotEventCount, _streamIdPrefix);
    }
}