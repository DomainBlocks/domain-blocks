namespace DomainBlocks.EventSourcing;

/// <summary>
/// Loads and persists event-sourced state by event stream.
/// </summary>
/// <typeparam name="TStreamId">The type used to identify event streams.</typeparam>
/// <typeparam name="TStreamPos">The type used to identify positions within an event stream.</typeparam>
public interface IEventSourcedStateStore<in TStreamId, TStreamPos> where TStreamId : notnull where TStreamPos : notnull
{
    /// <summary>
    /// Loads state from the events in the specified stream.
    /// </summary>
    /// <typeparam name="TState">The state type to load.</typeparam>
    /// <param name="streamId">The identifier of the stream to load.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>
    /// The reconstructed state and its stream version. If the stream contains no events, the state is the initial state
    /// and the version has no value.
    /// </returns>
    Task<(TState State, Optional<TStreamPos> Version)> LoadAsync<TState>(
        TStreamId streamId,
        CancellationToken cancellationToken = default)
        where TState : notnull;

    /// <summary>
    /// Loads state from the specified stream and throws if the stream does not exist.
    /// </summary>
    /// <typeparam name="TState">The state type to load.</typeparam>
    /// <param name="streamId">The identifier of the stream to load.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The reconstructed state and its stream version.</returns>
    /// <exception cref="DomainBlocks.EventStore.Abstractions.StreamNotFoundException">
    /// The specified stream does not exist.
    /// </exception>
    Task<(TState State, TStreamPos Version)> LoadRequiredAsync<TState>(
        TStreamId streamId,
        CancellationToken cancellationToken = default)
        where TState : notnull;

    /// <summary>
    /// Persists any uncommitted events that have been applied to the specified state.
    /// </summary>
    /// <typeparam name="TState">The state type to persist.</typeparam>
    /// <param name="state">The state whose uncommitted events will be persisted.</param>
    /// <param name="expectedVersion">The expected current stream version.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <exception cref="DomainBlocks.EventStore.Abstractions.StreamAppendConflictException{TStreamPos}">
    /// The stream is not at the expected version.
    /// </exception>
    Task SaveAsync<TState>(
        TState state,
        Optional<TStreamPos> expectedVersion,
        CancellationToken cancellationToken = default)
        where TState : notnull;

    /// <summary>
    /// Persists any uncommitted events that have been applied to the specified new state. The stream is expected to
    /// not exist.
    /// </summary>
    /// <typeparam name="TState">The state type to persist.</typeparam>
    /// <param name="state">The new state whose uncommitted events will be persisted.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <exception cref="DomainBlocks.EventStore.Abstractions.StreamAppendConflictException{TStreamPos}">
    /// The stream already exists at some version.
    /// </exception>
    Task SaveNewAsync<TState>(TState state, CancellationToken cancellationToken = default) where TState : notnull;
}