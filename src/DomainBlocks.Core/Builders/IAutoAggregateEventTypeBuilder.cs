namespace DomainBlocks.Core.Builders;

internal interface IAutoAggregateEventTypeBuilder<TAggregate>
{
    IEnumerable<AggregateEventType<TAggregate>> Build();
}