using DomainBlocks.Experimental.Persistence.Extensions;
using DomainBlocks.Experimental.Persistence.Serialization;

namespace DomainBlocks.Experimental.Persistence.Configuration;

public class EntityStreamConfigBuilder
{
    private readonly Type _entityType;
    private readonly List<IEventTypeMappingBuilder> _eventTypeMappingBuilders = new();
    private IEventDataSerializer? _eventDataSerializer;
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

    public EntityStreamConfigBuilder SetEventDataSerializer(IEventDataSerializer eventDataSerializer)
    {
        _eventDataSerializer = eventDataSerializer;
        return this;
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

        return new EntityStreamConfig(
            _entityType, eventTypeMap, _eventDataSerializer, _snapshotEventCount, _streamIdPrefix);
    }
}