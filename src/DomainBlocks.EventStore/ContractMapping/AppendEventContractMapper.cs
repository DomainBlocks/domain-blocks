namespace DomainBlocks.EventStore.ContractMapping;

public abstract class AppendEventContractMapper<TEventBase, TEvent, TContract> :
    IAppendEventContractMapper<TEventBase>
    where TEventBase : class
    where TEvent : TEventBase
    where TContract : notnull
{
    public Type EventType => typeof(TEvent);

    protected abstract TContract ToContract(TEvent @event);

    object IAppendEventContractMapper<TEventBase>.ToContract(TEventBase @event) => ToContract((TEvent)@event);
}