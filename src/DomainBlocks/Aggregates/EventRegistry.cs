namespace DomainBlocks.Aggregates;

public class EventRegistry<TEventBase>
{
    public EventRegistry(
        EventRoutes<TEventBase> eventRoutes,
        ImmutableEventRoutes<TEventBase> immutableEventRoutes,
        EventNameMap eventNameMap)
    {
        EventRoutes = eventRoutes;
        ImmutableEventRoutes = immutableEventRoutes;
        EventNameMap = eventNameMap;
    }

    public EventRoutes<TEventBase> EventRoutes { get; }
    public ImmutableEventRoutes<TEventBase> ImmutableEventRoutes { get; }
    public EventNameMap EventNameMap { get; }
}