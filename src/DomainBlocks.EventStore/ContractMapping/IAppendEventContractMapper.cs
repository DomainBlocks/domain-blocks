namespace DomainBlocks.EventStore.ContractMapping;

public interface IAppendEventContractMapper<in TEvent> where TEvent : notnull
{
    Type EventType { get; }

    object ToContract(TEvent @event);
}