namespace DomainBlocks.Core.Builders;

public interface IImmutableAggregateEventTypeBuilder<TAggregate, out TEvent> : IEventNameBuilder
{
    IEventNameBuilder ApplyWith(Func<TAggregate, TEvent, TAggregate> eventApplier);
}