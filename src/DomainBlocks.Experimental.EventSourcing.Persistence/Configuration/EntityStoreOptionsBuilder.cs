using DomainBlocks.Core.Serialization;
using DomainBlocks.Experimental.EventSourcing.Persistence.Extensions;

namespace DomainBlocks.Experimental.EventSourcing.Persistence.Configuration;

public class EntityStoreOptionsBuilder<TRawData>
{
    public EntityStoreOptionsBuilder()
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

    public EntityStoreOptionsBuilder<TRawData> SetEventDataSerializer(
        IEventDataSerializer<TRawData> eventDataSerializer)
    {
        EventDataSerializer = eventDataSerializer;
        return this;
    }

    public EntityStoreOptions<TRawData> Build()
    {
        if (EventDataSerializer == null)
        {
            throw new InvalidOperationException("Event data serializer not specified.");
        }

        return new EntityStoreOptions<TRawData>(EventDataSerializer);
    }
}

public class EntityStoreOptionsBuilder
{
    private readonly Dictionary<Type, IEventTypeMappingBuilder> _eventTypeMappingBuilders = new();
    private int? _snapshotEventCount;
    private readonly Dictionary<Type, EntityStreamOptionsBuilder> _entityStreamOptionsBuilders = new();

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

    public EntityStoreOptionsBuilder SetSnapshotEventCount(int? snapshotEventCount)
    {
        _snapshotEventCount = snapshotEventCount;
        return this;
    }

    public EntityStreamOptionsBuilder For<TEntity>()
    {
        if (_entityStreamOptionsBuilders.TryGetValue(typeof(TEntity), out var builder)) return builder;
        builder = new EntityStreamOptionsBuilder(typeof(TEntity));
        _entityStreamOptionsBuilders.Add(typeof(TEntity), builder);
        return builder;
    }

    public EntityStoreOptionsBuilder For<TEntity>(Action<EntityStreamOptionsBuilder> builderAction)
    {
        var builder = For<TEntity>();
        builderAction(builder);
        return this;
    }

    public EntityStoreOptions Build()
    {
        var eventTypeMap = _eventTypeMappingBuilders.Values.BuildEventTypeMap();
        var entityStreamOptions = _entityStreamOptionsBuilders.Values.Select(x => x.Build(eventTypeMap));
        return new EntityStoreOptions(eventTypeMap, _snapshotEventCount, entityStreamOptions);
    }
}