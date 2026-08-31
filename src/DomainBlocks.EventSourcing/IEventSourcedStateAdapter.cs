namespace DomainBlocks.EventSourcing;

// ReSharper disable UnusedTypeParameter - marker interface
public interface IEventSourcedStateAdapter<TEvent, out TStreamId>
// ReSharper restore UnusedTypeParameter
{
    Type StateType { get; }
}

public interface IEventSourcedStateAdapter<TState, TEvent, out TStreamId> :
    IEventSourcedStateAdapter<TEvent, TStreamId>
    where TState : notnull
    where TEvent : notnull
    where TStreamId : notnull
{
    Type IEventSourcedStateAdapter<TEvent, TStreamId>.StateType => typeof(TState);

    TState CreateInitialState();

    Task<TState> LoadAsync(TState initialState, IAsyncEnumerable<TEvent> events, CancellationToken cancellationToken);

    TStreamId GetStreamId(TState state);

    IEnumerable<TEvent> GetUncommittedEvents(TState state);
}