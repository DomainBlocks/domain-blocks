namespace DomainBlocks.Aggregates;

public static class EventRegistry
{
    public static EventRegistry<TEventBase> Create<TEventBase>(
        EventApplierMap<TEventBase> eventApplierMap,
        EventNameMap eventNameMap)
    {
        return new EventRegistry<TEventBase>(eventApplierMap, eventNameMap);
    }
}

public class EventRegistry<TEventBase>
{
    public EventRegistry(EventApplierMap<TEventBase> eventApplierMap, EventNameMap eventNameMap)
    {
        EventApplierMap = eventApplierMap;
        EventNameMap = eventNameMap;
    }

    public EventApplierMap<TEventBase> EventApplierMap { get; }
    public EventNameMap EventNameMap { get; }
}