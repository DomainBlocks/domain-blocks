using DomainBlocks.Experimental.EventSourcing.Persistence.Extensions;
using DomainBlocks.Experimental.EventSourcing.Persistence.Serialization;

namespace DomainBlocks.Experimental.EventSourcing.Persistence.Configuration;

public class EntityStoreConfigBuilder<TRawData>
{
    public EntityStoreConfigBuilder()
    {
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

    public EntityStoreConfigBuilder<TRawData> SetEventDataSerializer(
        IEventDataSerializer<TRawData> eventDataSerializer)
    {
        EventDataSerializer = eventDataSerializer;
        return this;
    }

    public EntityStoreConfig<TRawData> Build()
    {
        if (EventDataSerializer == null)
        {
            throw new InvalidOperationException("Event data serializer not specified.");
        }

        return new EntityStoreConfig<TRawData>(EventDataSerializer);
    }
}

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

    public SingleEventTypeMappingBuilder<TEvent> MapEventType<TEvent>()
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