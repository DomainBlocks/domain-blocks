namespace DomainBlocks.EventStore.ContractMapping;

public interface IEventContractMapper<TEvent> :
    IAppendEventContractMapper<TEvent>,
    IReadEventContractMapper<TEvent>
    where TEvent : notnull;