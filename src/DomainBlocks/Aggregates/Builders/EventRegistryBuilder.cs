using System;

namespace DomainBlocks.Aggregates.Builders;

public static class EventRegistryBuilder
{
    public static EventRegistryBuilder<TEventBase> OfType<TEventBase>()
    {
        return new EventRegistryBuilder<TEventBase>();
    }
}

public class EventRegistryBuilder<TEventBase>
{
    internal EventApplierMap<TEventBase> EventApplierMap { get; } = new();
    internal EventNameMap EventNameMap { get; } = new();

    public EventRegistryBuilder<TEventBase> For<TAggregate>(
        Action<EventRegistryBuilder<TAggregate, TEventBase>> builderAction)
    {
        var builder = new EventRegistryBuilder<TAggregate, TEventBase>(this);
        builderAction(builder);
        return this;
    }
    
    public EventRegistry<TEventBase> Build() => EventRegistry.Create(EventApplierMap, EventNameMap);
}

public class EventRegistryBuilder<TAggregate, TEventBase>
{
    private readonly EventRegistryBuilder<TEventBase> _builder;

    public EventRegistryBuilder(EventRegistryBuilder<TEventBase> builder)
    {
        _builder = builder;
    }

    internal EventNameMap EventNameMap => _builder.EventNameMap;

    public EventRegistrationBuilder<TAggregate, TEventBase, TEvent> Event<TEvent>() where TEvent : TEventBase
    {
        return new EventRegistrationBuilder<TAggregate, TEventBase, TEvent>(this);
    }

    public EventRegistryBuilder<TAggregate, TEventBase> ApplyWith(Action<TAggregate, TEventBase> eventApplier)
    {
        _builder.EventApplierMap.Add<TAggregate>((state, @event) =>
        {
            eventApplier(state, @event);
            return state;
        });

        return this;
    }

    public EventRegistryBuilder<TAggregate, TEventBase> ApplyWith(Func<TAggregate, TEventBase, TAggregate> eventApplier)
    {
        _builder.EventApplierMap.Add(eventApplier);
        return this;
    }
}