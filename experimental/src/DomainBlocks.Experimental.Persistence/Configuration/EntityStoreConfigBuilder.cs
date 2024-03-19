using DomainBlocks.Experimental.Persistence.Extensions;
using DomainBlocks.Experimental.Persistence.Serialization;

namespace DomainBlocks.Experimental.Persistence.Configuration;

public class EntityStoreConfigBuilder
{
    private readonly Dictionary<Type, IEventTypeMappingBuilder> _eventTypeMappingBuilders = new();
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
        return new EntityStoreConfig(eventTypeMap, _snapshotEventCount, entityStreamConfigs);
    }
}

public class EntityStoreConfigBuilder<TSerializedData>
{
    private IEventDataSerializer<TSerializedData>? _eventDataSerializer;
    private readonly Dictionary<Type, EntityStreamConfigBuilder<TSerializedData>> _entityStreamConfigBuilders = new();

    public EntityStoreConfigBuilder()
    {
        if (typeof(TSerializedData) == typeof(ReadOnlyMemory<byte>))
        {
            _eventDataSerializer = (IEventDataSerializer<TSerializedData>)new JsonBytesEventDataSerializer();
        }
        else if (typeof(TSerializedData) == typeof(string))
        {
            _eventDataSerializer = (IEventDataSerializer<TSerializedData>)new JsonStringEventDataSerializer();
        }
    }

    public EntityStoreConfigBuilder<TSerializedData> SetEventDataSerializer(
        IEventDataSerializer<TSerializedData> eventDataSerializer)
    {
        _eventDataSerializer = eventDataSerializer;
        return this;
    }

    public EntityStreamConfigBuilder<TSerializedData> For<TEntity>()
    {
        if (_entityStreamConfigBuilders.TryGetValue(typeof(TEntity), out var builder)) return builder;
        builder = new EntityStreamConfigBuilder<TSerializedData>(typeof(TEntity));
        _entityStreamConfigBuilders.Add(typeof(TEntity), builder);
        return builder;
    }

    public EntityStoreConfigBuilder<TSerializedData> For<TEntity>(
        Action<EntityStreamConfigBuilder<TSerializedData>> builderAction)
    {
        var builder = For<TEntity>();
        builderAction(builder);
        return this;
    }

    public EntityStoreConfig<TSerializedData> Build()
    {
        if (_eventDataSerializer == null)
        {
            throw new InvalidOperationException("Event data serializer not specified.");
        }

        var entityStreamConfigs = _entityStreamConfigBuilders.Values.Select(x => x.Build());
        return new EntityStoreConfig<TSerializedData>(_eventDataSerializer, entityStreamConfigs);
    }
}