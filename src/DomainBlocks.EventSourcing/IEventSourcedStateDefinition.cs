namespace DomainBlocks.EventSourcing;

public interface IEventSourcedStateDefinition<TState, TEvent, TStreamId>
    where TState : notnull
    where TEvent : notnull
    where TStreamId : notnull
{
    TState CreateInitialState();

    Task<TState> RestoreAsync(
        TState initialState,
        IAsyncEnumerable<TEvent> events,
        CancellationToken cancellationToken);

    TStreamId GetStreamId(TState state);

    IEnumerable<TEvent> GetUncommittedEvents(TState state);
}