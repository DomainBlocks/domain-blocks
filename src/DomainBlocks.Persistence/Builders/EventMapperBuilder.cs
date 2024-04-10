using DomainBlocks.Persistence.Events;
using DomainBlocks.Persistence.Serialization;

namespace DomainBlocks.Persistence.Builders;

public class EventMapperBuilder
{
    private readonly Dictionary<Type, IEventTypeMappingBuilder> _eventBaseTypeMappingBuilders = new();
    private readonly Dictionary<Type, IEventTypeMappingBuilder> _singleEventTypeMappingBuilders = new();
    private ISerializer _serializer = new JsonSerializer();

    public EventBaseTypeMappingBuilder<TEventBase> MapAll<TEventBase>()
    {
        if (_eventBaseTypeMappingBuilders.TryGetValue(typeof(TEventBase), out var builder))
        {
            return (EventBaseTypeMappingBuilder<TEventBase>)builder;
        }

        var newBuilder = new EventBaseTypeMappingBuilder<TEventBase>();
        _eventBaseTypeMappingBuilders.Add(typeof(TEventBase), newBuilder);

        return newBuilder;
    }

    public EventMapperBuilder MapAll<TEventBase>(Action<EventBaseTypeMappingBuilder<TEventBase>> builderAction)
    {
        var builder = MapAll<TEventBase>();
        builderAction(builder);
        return this;
    }

    public SingleEventTypeMappingBuilder<TEvent> Map<TEvent>()
    {
        if (_singleEventTypeMappingBuilders.TryGetValue(typeof(TEvent), out var builder))
        {
            return (SingleEventTypeMappingBuilder<TEvent>)builder;
        }

        var newBuilder = new SingleEventTypeMappingBuilder<TEvent>();
        _singleEventTypeMappingBuilders.Add(typeof(TEvent), newBuilder);

        return newBuilder;
    }

    public EventMapperBuilder Map<TEvent>(Action<SingleEventTypeMappingBuilder<TEvent>> builderAction)
    {
        var builder = Map<TEvent>();
        builderAction(builder);
        return this;
    }

    public EventMapperBuilder SetSerializer(ISerializer serializer)
    {
        _serializer = serializer;
        return this;
    }

    public EventMapper Build()
    {
        var eventBaseTypeMappings = _eventBaseTypeMappingBuilders.Values.SelectMany(x => x.Build());
        var singleEventTypeMappings = _singleEventTypeMappingBuilders.Values.SelectMany(x => x.Build());

        // Include mappings from event base type builders first so they can be overriden.
        var eventTypeMappings = eventBaseTypeMappings
            .Concat(singleEventTypeMappings)
            .GroupBy(x => x.EventType)
            .Select(x => x.Aggregate((_, next) => next));

        return new EventMapper(eventTypeMappings, _serializer);
    }
}