namespace DomainBlocks.Core.Builders;

public interface IEventOptionsBuilder<TAggregate>
{
    IEventOptions<TAggregate> Options { get; }
}