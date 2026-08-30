namespace DomainBlocks.EventSourcing;

public interface IEventSourcedStateStore<TStreamId, TStreamPos> where TStreamId : notnull where TStreamPos : notnull
{
    Task<EventSourcedState<TState, TStreamPos>> LoadAsync<TState>(
        TStreamId streamId,
        CancellationToken cancellationToken = default)
        where TState : notnull;

    Task<EventSourcedState<TState, TStreamPos>> LoadOrCreateAsync<TState>(
        TStreamId streamId,
        CancellationToken cancellationToken = default)
        where TState : notnull;

    Task SaveAsync<TState>(
        EventSourcedState<TState, TStreamPos> state,
        CancellationToken cancellationToken = default)
        where TState : notnull;
}