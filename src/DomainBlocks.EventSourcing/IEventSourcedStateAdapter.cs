namespace DomainBlocks.EventSourcing;

/// <summary>
/// Defines how event-sourced state maps to its stream and events.
/// </summary>
/// <typeparam name="TState">The type of the state.</typeparam>
/// <typeparam name="TEvent">The type of the events.</typeparam>
/// <typeparam name="TStreamId">The type of a stream ID.</typeparam>
public interface IEventSourcedStateAdapter<TState, TEvent, out TStreamId>
    where TState : notnull
    where TEvent : notnull
    where TStreamId : notnull
{
    /// <summary>
    /// Creates the state before any events are applied.
    /// </summary>
    TState CreateInitialState();

    /// <summary>
    /// Loads state by applying events to the initial state.
    /// </summary>
    Task<TState> LoadAsync(TState initialState, IAsyncEnumerable<TEvent> events, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the ID of the state's stream.
    /// </summary>
    TStreamId GetStreamId(TState state);

    /// <summary>
    /// Gets the events applied to the state that are not yet saved.
    /// </summary>
    IEnumerable<TEvent> GetUncommittedEvents(TState state);
}