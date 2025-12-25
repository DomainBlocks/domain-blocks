namespace DomainBlocks.EventStore;

public abstract class EventContractMapper<TEventBase, TEvent, TContract> :
    IEventContractMapper<TEventBase>
    where TEventBase : class
    where TEvent : TEventBase
    where TContract : notnull

{
    public Type EventType => typeof(TEvent);
    public Type ContractType => typeof(TContract);

    protected abstract TContract ToContract(TEvent @event);
    protected abstract TEvent FromContract(TContract contract);
    object IEventContractMapper<TEventBase>.ToContract(TEventBase @event) => ToContract((TEvent)@event);
    TEventBase IEventContractMapper<TEventBase>.FromContract(object contract) => FromContract((TContract)contract);
}