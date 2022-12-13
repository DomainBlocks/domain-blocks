namespace DomainBlocks.Core.Builders;

internal interface IEventOptionsBuilder<TAggregate>
{
    EventOptions<TAggregate> Options { get; }
}