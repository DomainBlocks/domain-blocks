namespace DomainBlocks.EventSourcing;

public interface IEventSourcedStateDefinitionProvider<TEvent, TStreamId>
    where TEvent : notnull
    where TStreamId : notnull
{
    IEventSourcedStateDefinition<TState, TEvent, TStreamId>? GetDefinition<TState>() where TState : notnull;
}