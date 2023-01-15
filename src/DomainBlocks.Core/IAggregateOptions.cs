namespace DomainBlocks.Core;

public interface IAggregateOptions<TAggregate> : IEntityType<TAggregate>
{
    ICommandExecutionContext<TAggregate> CreateCommandExecutionContext(TAggregate aggregate);
    TAggregate ApplyEvent(TAggregate aggregate, object @event);
}