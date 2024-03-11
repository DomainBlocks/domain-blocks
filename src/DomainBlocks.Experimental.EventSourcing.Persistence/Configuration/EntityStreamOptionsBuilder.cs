using DomainBlocks.Core.Serialization;
using DomainBlocks.Experimental.EventSourcing.Persistence.Extensions;

namespace DomainBlocks.Experimental.EventSourcing.Persistence.Configuration;

public class EntityStreamOptionsBuilder<TRawData>
{
    private readonly Type _entityType;

    public EntityStreamOptionsBuilder(Type entityType)
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

    public EntityStreamOptionsBuilder<TRawData> SetEventDataSerializer(
        IEventDataSerializer<TRawData> eventDataSerializer)
    {
        EventDataSerializer = eventDataSerializer;
        return this;
    }

    public EntityStreamOptions<TRawData> Build()
    {
        if (EventDataSerializer == null)
        {
            throw new InvalidOperationException("Event data serializer not specified.");
        }

        return new EntityStreamOptions<TRawData>(_entityType, EventDataSerializer);
    }
}

public class EntityStreamOptionsBuilder
{
    private readonly Type _entityType;
    private readonly List<IEventTypeMappingBuilder> _eventTypeMappingBuilders = new();
    private int? _snapshotEventCount;
    private string _streamIdPrefix;

    public EntityStreamOptionsBuilder(Type entityType)
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

    public EntityStreamOptionsBuilder SetSnapshotEventCount(int? eventCount)
    {
        _snapshotEventCount = eventCount;
        return this;
    }

    public EntityStreamOptionsBuilder SetStreamIdPrefix(string streamIdPrefix)
    {
        if (string.IsNullOrWhiteSpace(streamIdPrefix))
            throw new ArgumentException("Stream ID prefix cannot be null or whitespace.", nameof(streamIdPrefix));

        _streamIdPrefix = streamIdPrefix;
        return this;
    }

    public EntityStreamOptions Build(IEnumerable<EventTypeMapping>? eventTypeMappings = null)
    {
        var eventTypeMap = (eventTypeMappings ?? Enumerable.Empty<EventTypeMapping>())
            .AddOrReplaceWith(_eventTypeMappingBuilders.BuildEventTypeMap())
            .ToEventTypeMap();

        return new EntityStreamOptions(_entityType, eventTypeMap, _snapshotEventCount, _streamIdPrefix);
    }
}