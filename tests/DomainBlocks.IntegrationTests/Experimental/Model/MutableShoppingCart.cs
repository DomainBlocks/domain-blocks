using Shopping.Domain.Events;

namespace DomainBlocks.IntegrationTests.Experimental.Model;

public class MutableShoppingCart : MutableEntityBase
{
    private Guid _id = Guid.Empty;
    private readonly List<ShoppingCartItem> _items = new();

    public override Guid Id => _id;

    public IReadOnlyList<ShoppingCartItem> Items => _items;

    public void AddItem(ShoppingCartItem item)
    {
        if (Id == Guid.Empty)
        {
            Raise(new ShoppingCartCreated(Guid.NewGuid()));
        }

        Raise(new ItemAddedToShoppingCart(item.Id, Id, item.Name));
    }

    public void Apply(ShoppingCartCreated @event)
    {
        _id = @event.Id;
    }

    public void Apply(ItemAddedToShoppingCart @event)
    {
        _items.Add(new ShoppingCartItem(@event.Id, @event.Item));
    }
}