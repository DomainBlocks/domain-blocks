using DomainBlocks.Experimental.Persistence.Entities;
using DomainBlocks.Experimental.Persistence.Tests.Integration.Model;

namespace DomainBlocks.Experimental.Persistence.Tests.Integration.Adapters;

public class MutableShoppingCartEntityAdapter : IEntityAdapter<MutableShoppingCart>
{
    public Type StateType => typeof(MutableShoppingCart);
    public string GetId(MutableShoppingCart entity) => entity.Id.ToString();
    public object GetCurrentState(MutableShoppingCart entity) => entity;
    public IEnumerable<object> GetRaisedEvents(MutableShoppingCart entity) => entity.RaisedEvents;
    public object CreateState() => new MutableShoppingCart();

    public async Task<MutableShoppingCart> RestoreEntity(
        object initialState, IAsyncEnumerable<object> events, CancellationToken cancellationToken)
    {
        var shoppingCart = (MutableShoppingCart)initialState;

        await foreach (var e in events.WithCancellation(cancellationToken))
        {
            shoppingCart.Apply(e);
        }

        return shoppingCart;
    }
}