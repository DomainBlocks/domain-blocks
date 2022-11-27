using System;

namespace DomainBlocks.Core.Builders;

public sealed class ImmutableEventOptionsBuilder<TAggregate, TEvent> : IEventOptionsBuilder<TAggregate>
{
    private EventOptions<TAggregate, TEvent> _options = new();

    IEventOptions<TAggregate> IEventOptionsBuilder<TAggregate>.Options => _options;

    public ImmutableEventOptionsBuilder<TAggregate, TEvent> ApplyWith(
        Func<TAggregate, TEvent, TAggregate> eventApplier)
    {
        _options = _options.WithEventApplier(eventApplier);
        return this;
    }

    public ImmutableEventOptionsBuilder<TAggregate, TEvent> HasName(string eventName)
    {
        _options = _options.WithEventName(eventName);
        return this;
    }
}