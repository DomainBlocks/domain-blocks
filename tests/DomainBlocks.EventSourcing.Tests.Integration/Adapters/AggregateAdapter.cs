using DomainBlocks.EventSourcing.Tests.Integration.DomainEvents;
using DomainBlocks.EventSourcing.Tests.Integration.DomainModel;

namespace DomainBlocks.EventSourcing.Tests.Integration.Adapters;

public sealed class AggregateAdapter<TAggregate, TState> :
    IEventSourcedStateAdapter<TAggregate, IDomainEvent, string>
    where TAggregate : Aggregate<TState>, new()
    where TState : StateBase<TState>, new()
{
    public TAggregate CreateInitialState() => new();

    public async Task<TAggregate> LoadAsync(
        TAggregate initialState,
        IAsyncEnumerable<IDomainEvent> events,
        CancellationToken cancellationToken)
    {
        var loadedState = await events.AggregateAsync(
            initialState.State,
            (acc, next) => acc.Apply(next),
            cancellationToken);

        return new TAggregate { State = loadedState };
    }

    public string GetStreamId(TAggregate state) => state.Id;

    public IEnumerable<IDomainEvent> GetUncommittedEvents(TAggregate state) => state.UncommittedEvents;
}