using DomainBlocks.Experimental.EventSourcing;
using Shopping.Domain.Aggregates;
using Shopping.Domain.Events;

namespace DomainBlocks.IntegrationTests.Experimental;

public class ImmutableShoppingCartV2 : EventSourcedStateBase<ImmutableShoppingCartV2>
{
    private readonly List<ShoppingCartItem> _items = new();

    public ImmutableShoppingCartV2()
    {
    }

    private ImmutableShoppingCartV2(ImmutableShoppingCartV2 copyFrom)
    {
        _items = new List<ShoppingCartItem>(copyFrom._items);
        Id = copyFrom.Id;
        NewField = copyFrom.NewField;
    }

    public Guid? Id { get; private init; }
    public IReadOnlyList<ShoppingCartItem> Items => _items;
    public string? NewField { get; private init; }

    public ImmutableShoppingCartV2 AddItem(Guid cardId, Guid itemId, string itemName, string? newFieldValue = null)
    {
        var state = this;

        if (Id == null)
        {
            state = state.RaiseEvent(new ShoppingCartCreatedV2(cardId, newFieldValue));
        }

        state = state.RaiseEvent(new ItemAddedToShoppingCart(itemId, state.Id!.Value, itemName));

        return state;
    }

    protected override void OnConfiguring(EventTypeMap<ImmutableShoppingCartV2>.Builder eventTypes)
    {
        eventTypes
            .MapAll<IDomainEvent>()
            .WithApplierMethodName(nameof(Apply))
            .UseNonPublicMethods();
    }

    private ImmutableShoppingCartV2 Apply(ShoppingCartCreatedV2 e)
    {
        return new ImmutableShoppingCartV2(this) { Id = e.Id, NewField = e.NewField };
    }

    private ImmutableShoppingCartV2 Apply(ItemAddedToShoppingCart e)
    {
        var copy = new ImmutableShoppingCartV2(this);
        copy._items.Add(new ShoppingCartItem(e.Id, e.Item));
        return copy;
    }
}