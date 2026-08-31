using DomainBlocks.EventSourcing.Tests.Integration.DomainEvents;
using DomainBlocks.EventSourcing.Tests.Integration.DomainModel;

namespace DomainBlocks.EventSourcing.Tests.Integration.Adapters;

public sealed class MutableAggregateAdapter<TAggregate> : IEventSourcedStateAdapter<TAggregate, IDomainEvent, string>
    where TAggregate : MutableAggregateBase, new()
{
    public TAggregate CreateInitialState() => new();

    public async Task<TAggregate> LoadAsync(
        TAggregate initialState,
        IAsyncEnumerable<IDomainEvent> events,
        CancellationToken cancellationToken)
    {
        await foreach (var e in events.WithCancellation(cancellationToken).ConfigureAwait(false))
            initialState.Apply(e);

        return initialState;
    }

    public string GetStreamId(TAggregate state) => state.Id.ToString();

    public IEnumerable<IDomainEvent> GetUncommittedEvents(TAggregate state) => state.RaisedEvents;
}