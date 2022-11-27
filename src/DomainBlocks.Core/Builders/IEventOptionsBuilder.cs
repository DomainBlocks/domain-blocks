namespace DomainBlocks.Core.Builders;

internal interface IEventOptionsBuilder<TAggregate, TEventBase>
{
    EventOptions<TAggregate, TEventBase> Options { get; }
}