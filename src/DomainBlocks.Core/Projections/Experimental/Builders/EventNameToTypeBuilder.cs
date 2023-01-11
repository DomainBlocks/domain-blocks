namespace DomainBlocks.Core.Projections.Experimental.Builders;

public sealed class EventNameToTypeBuilder
{
    private readonly EventTypeMapBuilder _eventTypeMapBuilder;
    private readonly string _eventName;

    internal EventNameToTypeBuilder(EventTypeMapBuilder eventTypeMapBuilder, string eventName)
    {
        _eventTypeMapBuilder = eventTypeMapBuilder;
        _eventName = eventName;
    }

    public void To<TEvent>()
    {
        _eventTypeMapBuilder.EventTypeMap = _eventTypeMapBuilder.EventTypeMap.Add(typeof(TEvent), _eventName);
    }
}