namespace DomainBlocks.EventStore;

public interface IEventContractMapper
{
    Type EventType { get; }
    Type ContractType { get; }

    object ToContract(object @event);
    object FromContract(object contract);
}