using DomainBlocks.EventSourcing.Tests.Integration.DomainEvents;
using DomainBlocks.EventSourcing.Tests.Integration.DomainModel;

namespace DomainBlocks.EventSourcing.Tests.Integration.Adapters;

public sealed class MutableShoppingCartAdapter : IEventSourcedStateAdapter<MutableShoppingCart, IDomainEvent, string>
{
    public MutableShoppingCart CreateInitialState() => new();

    public async Task<MutableShoppingCart> LoadAsync(
        MutableShoppingCart initialState,
        IAsyncEnumerable<IDomainEvent> events,
        CancellationToken cancellationToken)
    {
        await foreach (var e in events.WithCancellation(cancellationToken))
            initialState.Apply(e);

        return initialState;
    }

    public string GetStreamId(MutableShoppingCart state) => state.Id.ToString();

    public IEnumerable<IDomainEvent> GetUncommittedEvents(MutableShoppingCart state) => state.RaisedEvents;
}