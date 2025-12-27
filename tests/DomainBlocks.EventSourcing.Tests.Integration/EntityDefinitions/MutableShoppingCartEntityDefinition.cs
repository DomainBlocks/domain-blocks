using DomainBlocks.EventSourcing.Tests.Integration.DomainEvents;
using DomainBlocks.EventSourcing.Tests.Integration.DomainModel;

namespace DomainBlocks.EventSourcing.Tests.Integration.EntityDefinitions;

public class MutableShoppingCartEntityDefinition : IEntityDefinition<IDomainEvent, MutableShoppingCart>
{
    public Type StateType => typeof(MutableShoppingCart);
    public string GetId(MutableShoppingCart entity) => entity.Id.ToString();
    public object GetState(MutableShoppingCart entity) => entity;
    public IEnumerable<IDomainEvent> GetUncommittedEvents(MutableShoppingCart entity) => entity.RaisedEvents;
    public object CreateInitialState() => new MutableShoppingCart();

    public async Task<MutableShoppingCart> RestoreAsync(
        object initialState,
        IAsyncEnumerable<IDomainEvent> events,
        CancellationToken cancellationToken)
    {
        var shoppingCart = (MutableShoppingCart)initialState;

        await foreach (var e in events.WithCancellation(cancellationToken))
            shoppingCart.Apply(e);

        return shoppingCart;
    }
}