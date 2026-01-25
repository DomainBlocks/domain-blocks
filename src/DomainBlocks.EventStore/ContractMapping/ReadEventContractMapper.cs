namespace DomainBlocks.EventStore.ContractMapping;

public abstract class ReadEventContractMapper<TEventBase, TEvent, TContract> :
    IReadEventContractMapper<TEventBase>
    where TEventBase : class
    where TEvent : TEventBase
    where TContract : notnull

{
    public Type ContractType => typeof(TContract);

    protected abstract TEvent FromContract(TContract contract);

    TEventBase IReadEventContractMapper<TEventBase>.FromContract(object contract) => FromContract((TContract)contract);
}