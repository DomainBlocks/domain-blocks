using DomainBlocks.Core;

namespace DomainBlocks.EventSourcing;

/// <summary>
/// Defines a store that loads and saves versioned state, using optimistic concurrency.
/// </summary>
/// <typeparam name="TState">The type of the state.</typeparam>
/// <typeparam name="TStateId">The type of a state ID.</typeparam>
/// <typeparam name="TVersion">The type of a version.</typeparam>
public interface IVersionedStateStore<TState, in TStateId, TVersion>
    where TState : notnull
    where TStateId : notnull
    where TVersion : notnull
{
    /// <summary>
    /// Loads state.
    /// </summary>
    /// <param name="stateId">The ID of the state.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// The state and its version, or the initial state and no version if the state does not exist.
    /// </returns>
    Task<(TState State, Optional<TVersion> Version)> LoadAsync(
        TStateId stateId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads state that must exist.
    /// </summary>
    /// <param name="stateId">The ID of the state.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The state and its version.</returns>
    /// <exception cref="StateNotFoundException">The state does not exist.</exception>
    Task<(TState State, TVersion Version)> LoadRequiredAsync(
        TStateId stateId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the state's unsaved events if the stored state is at the expected version.
    /// </summary>
    /// <param name="state">The state to save.</param>
    /// <param name="expectedVersion">The version of the stored state, or no version if the state is new.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="VersionConflictException{TVersion}">
    /// The state has unsaved events, and the stored state is not at <paramref name="expectedVersion"/>.
    /// </exception>
    /// <remarks>
    /// If the state has no unsaved events, the method returns without checking the version.
    /// </remarks>
    Task SaveAsync(TState state, Optional<TVersion> expectedVersion, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the unsaved events of new state.
    /// </summary>
    /// <param name="state">The state to save.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="VersionConflictException{TVersion}">
    /// The state has unsaved events, and state with the same ID already exists.
    /// </exception>
    /// <remarks>
    /// If the state has no unsaved events, the method returns without checking whether the state already exists.
    /// </remarks>
    Task SaveNewAsync(TState state, CancellationToken cancellationToken = default);
}