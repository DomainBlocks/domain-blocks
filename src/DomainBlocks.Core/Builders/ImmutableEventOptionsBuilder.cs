using System;

namespace DomainBlocks.Core.Builders;

public sealed class ImmutableEventOptionsBuilder<TAggregate, TEventBase, TEvent> :
    IEventOptionsBuilder<TAggregate>
    where TEvent : TEventBase
{
    private EventOptions<TAggregate, TEventBase, TEvent> _options = new();

    EventOptions<TAggregate> IEventOptionsBuilder<TAggregate>.Options => _options.HideEventType();

    public ImmutableEventOptionsBuilder<TAggregate, TEventBase, TEvent> ApplyWith(
        Func<TAggregate, TEvent, TAggregate> eventApplier)
    {
        _options = _options.WithEventApplier(eventApplier);
        return this;
    }

    public ImmutableEventOptionsBuilder<TAggregate, TEventBase, TEvent> HasName(string eventName)
    {
        _options = _options.WithEventName(eventName);
        return this;
    }
}