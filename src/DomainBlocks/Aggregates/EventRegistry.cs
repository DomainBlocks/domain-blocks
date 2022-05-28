namespace DomainBlocks.Aggregates;

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