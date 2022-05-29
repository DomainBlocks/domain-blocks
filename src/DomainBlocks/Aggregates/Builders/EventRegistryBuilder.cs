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
    internal EventRoutes<TEventBase> EventRoutes { get; } = new();
    internal EventNameMap EventNameMap { get; } = new();

    public EventRegistryBuilder<TEventBase> For<TAggregate>(
        Action<EventRegistryBuilder<TAggregate, TEventBase>> builderAction)
    {
        var builder = new EventRegistryBuilder<TAggregate, TEventBase>(this);
        builderAction(builder);
        return this;
    }
    
    public EventRegistry<TEventBase> Build() => new(EventRoutes, EventNameMap);
}

public class EventRegistryBuilder<TAggregate, TEventBase>
{
    private readonly EventRegistryBuilder<TEventBase> _builder;

    public EventRegistryBuilder(EventRegistryBuilder<TEventBase> builder)
    {
        _builder = builder;
    }
    
    internal EventRoutes<TEventBase> EventRoutes => _builder.EventRoutes;
    internal EventNameMap EventNameMap => _builder.EventNameMap;

    public EventRegistrationBuilder<TAggregate, TEventBase, TEvent> Event<TEvent>() where TEvent : TEventBase
    {
        return new EventRegistrationBuilder<TAggregate, TEventBase, TEvent>(this);
    }
}