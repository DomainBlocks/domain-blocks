using DomainBlocks.Experimental.Persistence.Extensions;
using DomainBlocks.Experimental.Persistence.Serialization;

namespace DomainBlocks.Experimental.Persistence.Configuration;

public class EntityStoreConfigBuilder
{
    private readonly Dictionary<Type, IEventTypeMappingBuilder> _eventTypeMappingBuilders = new();
    private IEventDataSerializer _eventDataSerializer = new JsonEventDataSerializer();
    private int? _snapshotEventCount;
    private readonly Dictionary<Type, EntityStreamConfigBuilder> _entityStreamConfigBuilders = new();

    public EventBaseTypeMappingBuilder<TEventBase> MapEventsOfType<TEventBase>()
    {
        if (_eventTypeMappingBuilders.TryGetValue(typeof(TEventBase), out var builder))
        {
            // TODO: error handling
            return (EventBaseTypeMappingBuilder<TEventBase>)builder;
        }

        var newBuilder = new EventBaseTypeMappingBuilder<TEventBase>();
        _eventTypeMappingBuilders.Add(typeof(TEventBase), newBuilder);
        return newBuilder;
    }

    public SingleEventTypeMappingBuilder<TEvent> MapEvent<TEvent>()
    {
        if (_eventTypeMappingBuilders.TryGetValue(typeof(TEvent), out var builder))
        {
            // TODO: error handling
            return (SingleEventTypeMappingBuilder<TEvent>)builder;
        }

        var newBuilder = new SingleEventTypeMappingBuilder<TEvent>();
        _eventTypeMappingBuilders.Add(typeof(TEvent), newBuilder);
        return newBuilder;
    }

    public EntityStoreConfigBuilder SetEventDataSerializer(IEventDataSerializer eventDataSerializer)
    {
        _eventDataSerializer = eventDataSerializer;
        return this;
    }

    public EntityStoreConfigBuilder SetSnapshotEventCount(int? snapshotEventCount)
    {
        _snapshotEventCount = snapshotEventCount;
        return this;
    }

    public EntityStreamConfigBuilder For<TEntity>()
    {
        if (_entityStreamConfigBuilders.TryGetValue(typeof(TEntity), out var builder)) return builder;
        builder = new EntityStreamConfigBuilder(typeof(TEntity));
        _entityStreamConfigBuilders.Add(typeof(TEntity), builder);
        return builder;
    }

    public EntityStoreConfigBuilder For<TEntity>(Action<EntityStreamConfigBuilder> builderAction)
    {
        var builder = For<TEntity>();
        builderAction(builder);
        return this;
    }

    public EntityStoreConfig Build()
    {
        var eventTypeMap = _eventTypeMappingBuilders.Values.BuildEventTypeMap();
        var entityStreamConfigs = _entityStreamConfigBuilders.Values.Select(x => x.Build(eventTypeMap));
        return new EntityStoreConfig(eventTypeMap, _eventDataSerializer, _snapshotEventCount, entityStreamConfigs);
    }
}