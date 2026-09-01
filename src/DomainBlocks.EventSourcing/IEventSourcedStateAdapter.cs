namespace DomainBlocks.EventSourcing;

/// <summary>
/// Adapts event-sourced state of a given type to its events and stream identifier.
/// </summary>
/// <typeparam name="TState">The state type being adapted.</typeparam>
/// <typeparam name="TEvent">The event type applied by the adapter.</typeparam>
/// <typeparam name="TStreamId">The type of stream identifier used by the adapter.</typeparam>
public interface IEventSourcedStateAdapter<TState, TEvent, out TStreamId>
    where TState : notnull
    where TEvent : notnull
    where TStreamId : notnull
{
    /// <summary>
    /// Creates the initial state to which events can be applied.
    /// </summary>
    TState CreateInitialState();

    /// <summary>
    /// Loads state by applying the supplied events to the initial state.
    /// </summary>
    Task<TState> LoadAsync(TState initialState, IAsyncEnumerable<TEvent> events, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the identifier for the event stream representing the state.
    /// </summary>
    TStreamId GetStreamId(TState state);

    /// <summary>
    /// Gets any uncommitted events that have been applied to the state.
    /// </summary>
    IEnumerable<TEvent> GetUncommittedEvents(TState state);
}