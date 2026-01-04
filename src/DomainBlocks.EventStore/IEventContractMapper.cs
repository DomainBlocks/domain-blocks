namespace DomainBlocks.EventStore;

public interface IEventContractMapper<TEvent> where TEvent : notnull
{
    Type EventType { get; }
    Type ContractType { get; }

    object ToContract(TEvent @event);
    TEvent FromContract(object contract);
}