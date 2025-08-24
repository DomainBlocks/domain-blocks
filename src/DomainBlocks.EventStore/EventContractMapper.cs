namespace DomainBlocks.EventStore;

public abstract class EventContractMapper<TEvent, TContract> :
    IEventContractMapper
    where TEvent : notnull
    where TContract : notnull

{
    public Type EventType => typeof(TEvent);
    public Type ContractType => typeof(TContract);

    protected abstract TContract ToContract(TEvent @event);
    protected abstract TEvent FromContract(TContract contract);
    object IEventContractMapper.ToContract(object @event) => ToContract((TEvent)@event);
    object IEventContractMapper.FromContract(object contract) => FromContract((TContract)contract);
}