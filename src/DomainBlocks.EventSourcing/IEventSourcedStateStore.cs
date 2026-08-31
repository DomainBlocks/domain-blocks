namespace DomainBlocks.EventSourcing;

public interface IEventSourcedStateStore<in TStreamId, TStreamPos> where TStreamId : notnull where TStreamPos : notnull
{
    Task<(TState State, Optional<TStreamPos> Version)> LoadAsync<TState>(
        TStreamId streamId,
        CancellationToken cancellationToken = default)
        where TState : notnull;

    Task<(TState State, TStreamPos Version)> LoadRequiredAsync<TState>(
        TStreamId streamId,
        CancellationToken cancellationToken = default)
        where TState : notnull;

    Task SaveAsync<TState>(
        TState state,
        Optional<TStreamPos> expectedVersion,
        CancellationToken cancellationToken = default)
        where TState : notnull;

    Task SaveNewAsync<TState>(TState state, CancellationToken cancellationToken = default) where TState : notnull;
}