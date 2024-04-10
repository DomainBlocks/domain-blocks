using System.Collections.Immutable;
using Shopping.Domain.Events;

namespace DomainBlocks.Persistence.Tests.Integration.Model;

public record ShoppingCartState : StateBase<ShoppingCartState>
{
    static ShoppingCartState()
    {
        When<ShoppingSessionStarted>((s, e) => s with { SessionId = e.SessionId });
        When<ItemAddedToShoppingCart>((s, e) => s with { Items = s.Items.Add(new ShoppingCartItem(e.SessionId, e.Item)) });
    }

    public Guid SessionId { get; init; }
    public ImmutableList<ShoppingCartItem> Items { get; private init; } = ImmutableList<ShoppingCartItem>.Empty;
}