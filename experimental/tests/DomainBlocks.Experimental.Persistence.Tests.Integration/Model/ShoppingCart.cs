using Shopping.Domain.Events;

namespace DomainBlocks.Experimental.Persistence.Tests.Integration.Model;

public class ShoppingCart : EntityBase<ShoppingCartState>
{
    public override string Id => State.Id.ToString();

    public void AddItem(ShoppingCartItem item)
    {
        if (State.Id == Guid.Empty)
        {
            Raise(new ShoppingCartCreated(Guid.NewGuid()));
        }

        Raise(new ItemAddedToShoppingCart(item.Id, State.Id, item.Name));
    }
}

public class ShoppingCartV2 : EntityBase<ShoppingCartStateV2>
{
    public override string Id => State.Id.ToString();

    public void AddItem(ShoppingCartItem item, string? newFieldValue = null)
    {
        if (State.Id == Guid.Empty)
        {
            Raise(new ShoppingCartCreatedV2(Guid.NewGuid(), newFieldValue));
        }

        Raise(new ItemAddedToShoppingCart(item.Id, State.Id, item.Name));
    }
}