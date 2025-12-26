using DomainBlocks.EventSourcing.Tests.Integration.DomainEvents;
using DomainBlocks.EventSourcing.Tests.Integration.DomainModel;

namespace DomainBlocks.EventSourcing.Tests.Integration.EntityDefinitions;

public sealed class AggregateDefinition2<TAggregate, TState> : IEntityDefinition<IDomainEvent, TAggregate>
    where TAggregate : Aggregate<TState>, new()
    where TState : StateBase<TState>, new()
{
    public Type StateType => typeof(TState);
    public string GetId(TAggregate entity) => entity.Id;
    public object GetState(TAggregate entity) => entity.State;
    public IEnumerable<IDomainEvent> GetUncommittedEvents(TAggregate entity) => entity.UncommittedEvents;
    public object CreateInitialState() => new TState();

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