namespace DomainBlocks.Aggregates;

public static class EventRegistry
{
    public static EventRegistry<TEventBase> Create<TEventBase>(
        EventRoutes<TEventBase> eventRoutes, EventNameMap eventNameMap)
    {
        var eventRouter = new AggregateEventRouter<TEventBase>(eventRoutes);
        return new EventRegistry<TEventBase>(eventRouter, eventNameMap);
    }
}

public class EventRegistry<TEventBase>
{
    public EventRegistry(IAggregateEventRouter<TEventBase> eventRoutes, EventNameMap eventNameMap)
    {
        EventRouter = eventRoutes;
        EventNameMap = eventNameMap;
    }

    public IAggregateEventRouter<TEventBase> EventRouter { get; }
    public EventNameMap EventNameMap { get; }
}