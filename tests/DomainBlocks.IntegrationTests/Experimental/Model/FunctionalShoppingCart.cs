using System.Collections.Immutable;
using Shopping.Domain.Events;

namespace DomainBlocks.IntegrationTests.Experimental.Model;

public record FunctionalShoppingCart : IIdentifiable
{
    public Guid Id { get; private init; }
    public ImmutableList<ShoppingCartItem> Items { get; private init; } = ImmutableList<ShoppingCartItem>.Empty;

    public IEnumerable<object> AddItem(ShoppingCartItem item)
    {
        var currentState = this;

        if (Id == Guid.Empty)
        {
            var shoppingCartCreated = new ShoppingCartCreated(Guid.NewGuid());
            currentState = currentState.Apply(shoppingCartCreated);
            yield return shoppingCartCreated;
        }

        yield return new ItemAddedToShoppingCart(item.Id, currentState.Id, item.Name);
    }

    public FunctionalShoppingCart Apply(ShoppingCartCreated @event)
    {
        return this with { Id = @event.Id };
    }

    public FunctionalShoppingCart Apply(ItemAddedToShoppingCart @event)
    {
        return this with
        {
            Items = Items.Add(new ShoppingCartItem(@event.Id, @event.Item))
        };
    }
}