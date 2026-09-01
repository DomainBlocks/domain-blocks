namespace DomainBlocks.EventSourcing;

/// <summary>
/// Loads and persists versioned state by identifier using optimistic concurrency.
/// </summary>
/// <typeparam name="TState">The type of state stored by this store.</typeparam>
/// <typeparam name="TStateId">The type used to identify stored state.</typeparam>
/// <typeparam name="TVersion">The type used to identify a version of stored state.</typeparam>
public interface IVersionedStateStore<TState, in TStateId, TVersion>
    where TState : notnull
    where TStateId : notnull
    where TVersion : notnull
{
    /// <summary>
    /// Loads state by identifier.
    /// </summary>
    /// <param name="stateId">The identifier of the state to load.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>
    /// The state and its current version, or the initial state with no version if the state does not exist.
    /// </returns>
    Task<(TState State, Optional<TVersion> Version)> LoadAsync(
        TStateId stateId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads state by identifier and throws if it does not exist.
    /// </summary>
    /// <param name="stateId">The identifier of the state to load.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The state and its current version.</returns>
    /// <exception cref="StateNotFoundException">
    /// The state with ID <paramref name="stateId"/> does not exist.
    /// </exception>
    Task<(TState State, TVersion Version)> LoadRequiredAsync(
        TStateId stateId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists state if its current version matches the expected version.
    /// </summary>
    /// <param name="state">The state to persist.</param>
    /// <param name="expectedVersion">The expected version of the stored state.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <exception cref="VersionConflictException{TVersion}">
    /// The stored state is not at the expected version.
    /// </exception>
    Task SaveAsync(TState state, Optional<TVersion> expectedVersion, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists new state and throws if state with the same identifier already exists.
    /// </summary>
    /// <param name="state">The new state to persist.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <exception cref="VersionConflictException{TVersion}">
    /// State with the same identifier already exists.
    /// </exception>
    Task SaveNewAsync(TState state, CancellationToken cancellationToken = default);
}