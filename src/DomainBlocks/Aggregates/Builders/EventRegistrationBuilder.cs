using System;

namespace DomainBlocks.Aggregates.Builders;

public class EventRegistrationBuilder<TAggregate, TEventBase, TEvent> where TEvent : TEventBase
{
    private readonly EventRegistryBuilder<TAggregate, TEventBase> _eventRegistryBuilder;

    public EventRegistrationBuilder(EventRegistryBuilder<TAggregate, TEventBase> eventRegistryBuilder)
    {
        _eventRegistryBuilder = eventRegistryBuilder;
    }
    
    public EventRegistrationBuilder<TAggregate, TEventBase, TEvent> RoutesTo(Action<TAggregate, TEvent> eventApplier)
    {
        return RoutesTo((agg, e) =>
        {
            eventApplier(agg, e);
            return agg;
        });
    }

    public EventRegistrationBuilder<TAggregate, TEventBase, TEvent> RoutesTo(
        EventApplier<TAggregate, TEvent> eventApplier)
    {
        _eventRegistryBuilder.EventRoutes.Add(eventApplier);
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

    public EventRegistry<TEventBase> Build() => _eventRegistryBuilder.Build();
}