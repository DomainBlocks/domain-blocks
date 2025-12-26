using DomainBlocks.EventSourcing.Tests.Integration.DomainEvents;
using DomainBlocks.EventSourcing.Tests.Integration.DomainModel;

namespace DomainBlocks.EventSourcing.Tests.Integration.Adapters;

public sealed class AggregateAdapter2<TAggregate, TState> : IEntityAdapter<TAggregate, IDomainEvent>
    where TAggregate : Aggregate<TState>, new()
    where TState : StateBase<TState>, new()
{
    public Type StateType => typeof(TState);
    public string GetId(TAggregate entity) => entity.Id;
    public object GetCurrentState(TAggregate entity) => entity.State;
    public IEnumerable<IDomainEvent> GetUncommittedEvents(TAggregate entity) => entity.UncommittedEvents;
    public object CreateState() => new TState();

    public async Task<TAggregate> RestoreAsync(
        object initialState,
        IAsyncEnumerable<IDomainEvent> events,
        CancellationToken cancellationToken)
    {
        var state = (TState)initialState;

        await foreach (var e in events.WithCancellation(cancellationToken))
        {
            state = state.Apply(e);
        }

        return new TAggregate { State = state };
    }
}