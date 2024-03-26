using System.Collections.Immutable;
using Shopping.Domain.Events;

namespace DomainBlocks.Persistence.Tests.Integration.Model;

public record ShoppingCartState : StateBase<ShoppingCartState>
{
    static ShoppingCartState()
    {
        When<ShoppingCartCreated>((s, e) => s with { Id = e.Id });
        When<ItemAddedToShoppingCart>((s, e) => s with { Items = s.Items.Add(new ShoppingCartItem(e.Id, e.Item)) });
    }

    public Guid Id { get; init; }
    public ImmutableList<ShoppingCartItem> Items { get; private init; } = ImmutableList<ShoppingCartItem>.Empty;
}