namespace DomainBlocks.Core.Projections.Builders;

public sealed class EventNameToTypeBuilder
{
    private readonly EventTypeMapBuilder _eventTypeMapBuilder;
    private readonly string _eventName;

    internal EventNameToTypeBuilder(EventTypeMapBuilder eventTypeMapBuilder, string eventName)
    {
        _eventTypeMapBuilder = eventTypeMapBuilder;
        _eventName = eventName;
    }

    public void ToType<TEvent>()
    {
        _eventTypeMapBuilder.EventTypeMap = _eventTypeMapBuilder.EventTypeMap.Add(typeof(TEvent), _eventName);
    }
}