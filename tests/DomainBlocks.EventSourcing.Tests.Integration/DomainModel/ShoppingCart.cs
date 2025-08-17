using DomainBlocks.EventSourcing.Tests.Integration.DomainEvents;

namespace DomainBlocks.EventSourcing.Tests.Integration.DomainModel;

public class ShoppingCart : Aggregate<ShoppingCartState>
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