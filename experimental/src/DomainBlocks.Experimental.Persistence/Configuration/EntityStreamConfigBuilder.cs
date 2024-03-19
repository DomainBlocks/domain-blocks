using DomainBlocks.Experimental.Persistence.Extensions;
using DomainBlocks.Experimental.Persistence.Serialization;

namespace DomainBlocks.Experimental.Persistence.Configuration;

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

    public SingleEventTypeMappingBuilder<TEvent> MapEvent<TEvent>()
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

public class EntityStreamConfigBuilder<TSerializedData>
{
    private readonly Type _entityType;
    private IEventDataSerializer<TSerializedData>? _eventDataSerializer;

    public EntityStreamConfigBuilder(Type entityType)
    {
        _entityType = entityType;
    }

    public EntityStreamConfigBuilder<TSerializedData> SetEventDataSerializer(
        IEventDataSerializer<TSerializedData> eventDataSerializer)
    {
        _eventDataSerializer = eventDataSerializer;
        return this;
    }

    public EntityStreamConfig<TSerializedData> Build()
    {
        return new EntityStreamConfig<TSerializedData>(_entityType, _eventDataSerializer);
    }
}