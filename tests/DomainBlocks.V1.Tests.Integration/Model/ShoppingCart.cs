using Shopping.Domain.Events;

namespace DomainBlocks.V1.Tests.Integration.Model;

public class ShoppingCart : EntityBase<ShoppingCartState>
{
    public override string Id => State.SessionId.ToString();

    public void AddItem(ShoppingCartItem item)
    {
        if (State.SessionId == Guid.Empty)
        {
            Raise(new ShoppingSessionStarted(item.SessionId));
        }

        Raise(new ItemAddedToShoppingCart(item.SessionId, item.Name));
    }
}