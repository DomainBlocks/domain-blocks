using System.Collections.Immutable;
using Shopping.Domain.Events;

namespace DomainBlocks.V1.Tests.Integration.Model;

public record FunctionalShoppingCart : IIdentifiable
{
    public Guid Id { get; private init; }
    public ImmutableList<ShoppingCartItem> Items { get; private init; } = ImmutableList<ShoppingCartItem>.Empty;

    public IEnumerable<object> AddItem(ShoppingCartItem item)
    {
        var currentState = this;

        if (Id == Guid.Empty)
        {
            var shoppingSessionStarted = new ShoppingSessionStarted(Guid.NewGuid());
            currentState = currentState.Apply(shoppingSessionStarted);
            yield return shoppingSessionStarted;
        }

        yield return new ItemAddedToShoppingCart(currentState.Id, item.Name);
    }

    public FunctionalShoppingCart Apply(ShoppingSessionStarted @event)
    {
        return this with { Id = @event.SessionId };
    }

    public FunctionalShoppingCart Apply(ItemAddedToShoppingCart @event)
    {
        return this with
        {
            Items = Items.Add(new ShoppingCartItem(@event.SessionId, @event.Item))
        };
    }
}