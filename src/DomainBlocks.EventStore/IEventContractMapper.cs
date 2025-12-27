namespace DomainBlocks.EventStore;

public interface IEventContractMapper<TEventBase> where TEventBase : class
{
    Type EventType { get; }
    Type ContractType { get; }

    object ToContract(TEventBase @event);
    TEventBase FromContract(object contract);
}