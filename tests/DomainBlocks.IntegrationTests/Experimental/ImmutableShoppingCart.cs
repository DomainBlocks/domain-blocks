using DomainBlocks.Experimental.EventSourcing;
using Shopping.Domain.Aggregates;
using Shopping.Domain.Events;

namespace DomainBlocks.IntegrationTests.Experimental;

public class ImmutableShoppingCart : EventSourcedStateBase<ImmutableShoppingCart>
{
    private readonly List<ShoppingCartItem> _items = new();

    public ImmutableShoppingCart()
    {
    }

    private ImmutableShoppingCart(ImmutableShoppingCart copyFrom)
    {
        _items = new List<ShoppingCartItem>(copyFrom._items);
        Id = copyFrom.Id;
    }

    public Guid? Id { get; private init; }
    public IReadOnlyList<ShoppingCartItem> Items => _items;

    public ImmutableShoppingCart AddItem(Guid cardId, Guid itemId, string itemName)
    {
        var state = this;

        if (Id == null)
        {
            state = state.RaiseEvent(new ShoppingCartCreated(cardId));
        }

        state = state.RaiseEvent(new ItemAddedToShoppingCart(itemId, state.Id!.Value, itemName));

        return state;
    }

    protected override void OnConfiguring(EventTypeMap<ImmutableShoppingCart>.Builder eventTypes)
    {
        eventTypes
            .MapAll<IDomainEvent>()
            .WithApplierMethodName(nameof(Apply))
            .UseNonPublicMethods();
    }

    private ImmutableShoppingCart Apply(ShoppingCartCreated e)
    {
        return new ImmutableShoppingCart(this) { Id = e.Id };
    }

    private ImmutableShoppingCart Apply(ItemAddedToShoppingCart e)
    {
        var copy = new ImmutableShoppingCart(this);
        copy._items.Add(new ShoppingCartItem(e.Id, e.Item));
        return copy;
    }
}