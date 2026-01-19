namespace DomainBlocks.EventStore.ContractMapping;

public interface IReadEventContractMapper<out TEvent> where TEvent : notnull
{
    Type ContractType { get; }

    TEvent FromContract(object contract);
}