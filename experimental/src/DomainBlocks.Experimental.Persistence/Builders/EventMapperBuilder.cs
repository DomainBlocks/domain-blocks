using DomainBlocks.Experimental.Persistence.Events;
using DomainBlocks.Experimental.Persistence.Extensions;
using DomainBlocks.Experimental.Persistence.Serialization;

namespace DomainBlocks.Experimental.Persistence.Builders;

public class EventMapperBuilder
{
    private readonly Dictionary<Type, IEventTypeMappingBuilder> _eventTypeMappingBuilders = new();
    private IEventDataSerializer _serializer = new JsonEventDataSerializer();

    public EventMapperBuilder MapEventsOfType<TEventBase>(
        Action<EventBaseTypeMappingBuilder<TEventBase>>? builderAction = null)
    {
        if (_eventTypeMappingBuilders.TryGetValue(typeof(TEventBase), out var builder))
        {
            // TODO: error handling
            builderAction?.Invoke((EventBaseTypeMappingBuilder<TEventBase>)builder);
        }

        var newBuilder = new EventBaseTypeMappingBuilder<TEventBase>();
        _eventTypeMappingBuilders.Add(typeof(TEventBase), newBuilder);
        builderAction?.Invoke(newBuilder);

        return this;
    }

    public EventMapperBuilder MapEvent<TEvent>(Action<SingleEventTypeMappingBuilder<TEvent>>? builderAction = null)
    {
        if (_eventTypeMappingBuilders.TryGetValue(typeof(TEvent), out var builder))
        {
            // TODO: error handling
            builderAction?.Invoke((SingleEventTypeMappingBuilder<TEvent>)builder);
        }

        var newBuilder = new SingleEventTypeMappingBuilder<TEvent>();
        _eventTypeMappingBuilders.Add(typeof(TEvent), newBuilder);
        builderAction?.Invoke(newBuilder);

        return this;
    }

    public EventMapperBuilder ForStreamOf<TEntity>()
    {
        return this;
    }

    public EventMapperBuilder SetSerializer(IEventDataSerializer serializer)
    {
        _serializer = serializer;
        return this;
    }

    public EventMapper Build()
    {
        var eventTypeMap = _eventTypeMappingBuilders.Values.BuildAll();
        return new EventMapper(eventTypeMap, _serializer);
    }
}