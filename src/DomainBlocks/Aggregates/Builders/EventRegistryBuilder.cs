namespace DomainBlocks.Aggregates.Builders;

public static class EventRegistryBuilder
{
    public static EventRegistryBuilder<TAggregate, TEventBase> Create<TAggregate, TEventBase>()
    {
        return new EventRegistryBuilder<TAggregate, TEventBase>(
            new EventRoutes<TEventBase>(),
            new EventNameMap());
    }
}

public class EventRegistryBuilder<TAggregate, TEventBase>
{
    public EventRegistryBuilder(EventRoutes<TEventBase> eventRoutes, EventNameMap eventNameMap)
    {
        EventRoutes = eventRoutes;
        EventNameMap = eventNameMap;
    }

    internal EventRoutes<TEventBase> EventRoutes { get; }
    internal EventNameMap EventNameMap { get; }

    public EventRegistrationBuilder<TAggregate, TEventBase, TEvent> Event<TEvent>() where TEvent : TEventBase
    {
        return new EventRegistrationBuilder<TAggregate, TEventBase, TEvent>(this);
    }

    public EventRegistry<TEventBase> Build() => new(EventRoutes, EventNameMap);
}