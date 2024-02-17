namespace DomainBlocks.Experimental.EventSourcing;

public interface IEventTypeMapSource<TState>
{
    EventTypeMap<TState> EventTypeMap { get; }
}