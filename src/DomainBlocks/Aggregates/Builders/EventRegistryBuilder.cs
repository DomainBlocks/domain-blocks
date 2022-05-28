namespace DomainBlocks.Aggregates.Builders;

public static class EventRegistryBuilder
{
    public static EventRegistryBuilder<TAggregate, TEventBase> Create<TAggregate, TEventBase>()
    {
        return new EventRegistryBuilder<TAggregate, TEventBase>(
            new EventRoutes<TEventBase>(),
            new ImmutableEventRoutes<TEventBase>(),
            new EventNameMap());
    }
}

public class EventRegistryBuilder<TAggregate, TEventBase>
{
    public EventRegistryBuilder(
        EventRoutes<TEventBase> eventRoutes,
        ImmutableEventRoutes<TEventBase> immutableEventRoutes,
        EventNameMap eventNameMap)
    {
        EventRoutes = eventRoutes;
        ImmutableEventRoutes = immutableEventRoutes;
        EventNameMap = eventNameMap;
    }

    internal EventRoutes<TEventBase> EventRoutes { get; }
    internal ImmutableEventRoutes<TEventBase> ImmutableEventRoutes { get; }
    internal EventNameMap EventNameMap { get; }

    public EventRegistrationBuilder<TAggregate, TEventBase, TEvent> Event<TEvent>() where TEvent : TEventBase
    {
        return new EventRegistrationBuilder<TAggregate, TEventBase, TEvent>(this);
    }

    public EventRegistry<TEventBase> Build() => new(EventRoutes, ImmutableEventRoutes, EventNameMap);
}