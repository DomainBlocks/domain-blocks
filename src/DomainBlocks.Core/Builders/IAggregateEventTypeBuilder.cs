namespace DomainBlocks.Core.Builders;

internal interface IAggregateEventTypeBuilder<TAggregate>
{
    AggregateEventType<TAggregate> EventType { get; }
}