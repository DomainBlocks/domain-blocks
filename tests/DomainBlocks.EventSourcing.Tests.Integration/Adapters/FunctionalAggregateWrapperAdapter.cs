using DomainBlocks.EventSourcing.Tests.Integration.DomainEvents;
using DomainBlocks.EventSourcing.Tests.Integration.DomainModel;

namespace DomainBlocks.EventSourcing.Tests.Integration.Adapters;

public sealed class FunctionalAggregateWrapperAdapter<TAggregate> :
    IEventSourcedStateAdapter<FunctionalAggregateWrapper<TAggregate>, IDomainEvent, string>
    where TAggregate : IIdentifiable, new()
{
    public FunctionalAggregateWrapper<TAggregate> CreateInitialState() => new();

    public async Task<FunctionalAggregateWrapper<TAggregate>> LoadAsync(
        FunctionalAggregateWrapper<TAggregate> initialState,
        IAsyncEnumerable<IDomainEvent> events,
        CancellationToken cancellationToken)
    {
        await foreach (var e in events.WithCancellation(cancellationToken).ConfigureAwait(false))
            initialState.Apply(e);

        return initialState;
    }

    public string GetStreamId(FunctionalAggregateWrapper<TAggregate> state) => state.Id.ToString();

    public IEnumerable<IDomainEvent> GetUncommittedEvents(FunctionalAggregateWrapper<TAggregate> state) =>
        state.RaisedEvents;
}