using DomainBlocks.EventSourcing.Tests.Integration.DomainEvents;
using DomainBlocks.EventSourcing.Tests.Integration.DomainModel;

namespace DomainBlocks.EventSourcing.Tests.Integration.EntityDefinitions;

public sealed class AggregateDefinition3<TAggregate, TState> :
    IEventSourcedStateDefinition<TAggregate, IDomainEvent, string>
    where TAggregate : Aggregate<TState>, new()
    where TState : StateBase<TState>, new()
{
    public TAggregate CreateInitialState() => new();

    public async Task<TAggregate> RestoreAsync(
        TAggregate initialState,
        IAsyncEnumerable<IDomainEvent> events,
        CancellationToken cancellationToken)
    {
        var aggregateState = initialState.State;

        await foreach (var e in events.WithCancellation(cancellationToken).ConfigureAwait(false))
            aggregateState = aggregateState.Apply(e);

        return new TAggregate { State = aggregateState };
    }

    public string GetStreamId(TAggregate state) => state.Id;

    public IEnumerable<IDomainEvent> GetUncommittedEvents(TAggregate state) => state.UncommittedEvents;
}