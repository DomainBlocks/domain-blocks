namespace DomainBlocks.Aggregates;

public static class EventRegistry
{
    public static EventRegistry<TEventBase> Create<TEventBase>(
        EventRoutes<TEventBase> eventRoutes, EventNameMap eventNameMap)
    {
        return new EventRegistry<TEventBase>(eventRoutes, eventNameMap);
    }
}

public class EventRegistry<TEventBase>
{
    public EventRegistry(EventRoutes<TEventBase> eventRoutes, EventNameMap eventNameMap)
    {
        EventRoutes = eventRoutes;
        EventNameMap = eventNameMap;
    }

    public EventRoutes<TEventBase> EventRoutes { get; }
    public EventNameMap EventNameMap { get; }
}