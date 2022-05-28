namespace DomainBlocks.Aggregates.Builders;

public class EventRegistrationBuilder<TAggregate, TEventBase, TEvent> where TEvent : TEventBase
{
    private readonly EventRegistryBuilder<TAggregate, TEventBase> _eventRegistryBuilder;

    public EventRegistrationBuilder(EventRegistryBuilder<TAggregate, TEventBase> eventRegistryBuilder)
    {
        _eventRegistryBuilder = eventRegistryBuilder;
    }

    public EventRegistrationBuilder<TAggregate, TEventBase, TEvent> RoutesTo(ApplyEvent<TAggregate, TEvent> applyEvent)
    {
        _eventRegistryBuilder.EventRoutes.Add(
            (typeof(TAggregate), typeof(TEvent)), (agg, e) => applyEvent((TAggregate)agg, (TEvent)e));
        return this;
    }

    public EventRegistrationBuilder<TAggregate, TEventBase, TEvent> RoutesTo(
        ImmutableApplyEvent<TAggregate, TEvent> applyEvent)
    {
        _eventRegistryBuilder.ImmutableEventRoutes.Add(
            (typeof(TAggregate), typeof(TEvent)), (agg, e) => applyEvent((TAggregate)agg, (TEvent)e));
        return this;
    }
    
    public EventRegistrationBuilder<TAggregate, TEventBase, TEvent> HasName(string name)
    {
        _eventRegistryBuilder.EventNameMap.RegisterEvent<TEvent>(name);
        return this;
    }

    public EventRegistrationBuilder<TAggregate, TEventBase, TNextEvent> Event<TNextEvent>()
        where TNextEvent : TEventBase
    {
        return _eventRegistryBuilder.Event<TNextEvent>();
    }
}