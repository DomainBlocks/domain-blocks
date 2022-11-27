using System;

namespace DomainBlocks.Core.Builders;

public sealed class MutableEventOptionsBuilder<TAggregate, TEvent> : IEventOptionsBuilder<TAggregate>
{
    private EventOptions<TAggregate, TEvent> _options = new();

    IEventOptions<TAggregate> IEventOptionsBuilder<TAggregate>.Options => _options;

    public MutableEventOptionsBuilder<TAggregate, TEvent> ApplyWith(Action<TAggregate, TEvent> eventApplier)
    {
        _options = _options.WithEventApplier(eventApplier);
        return this;
    }

    public MutableEventOptionsBuilder<TAggregate, TEvent> HasName(string eventName)
    {
        _options = _options.WithEventName(eventName);
        return this;
    }
}