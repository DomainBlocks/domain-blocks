namespace DomainBlocks.EventSourcing;

public interface IEventSourcedStateAdapterResolver<TEvent, TStreamId> where TEvent : notnull where TStreamId : notnull
{
    IEventSourcedStateAdapter<TState, TEvent, TStreamId>? Resolve<TState>() where TState : notnull;
}