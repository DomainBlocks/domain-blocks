namespace DomainBlocks.Core.Builders;

public interface IMutableAggregateEventTypeBuilder<out TAggregate, out TEvent> : IEventNameBuilder
{
    IEventNameBuilder ApplyWith(Action<TAggregate, TEvent> eventApplier);
}