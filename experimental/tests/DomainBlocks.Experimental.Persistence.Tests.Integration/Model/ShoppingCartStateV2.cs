using System.Collections.Immutable;
using Shopping.Domain.Events;

namespace DomainBlocks.Experimental.Persistence.Tests.Integration.Model;

public record ShoppingCartStateV2 : StateBase<ShoppingCartStateV2>
{
    static ShoppingCartStateV2()
    {
        When<ShoppingCartCreatedV2>((s, e) => s with { Id = e.Id, NewField = e.NewField });
        When<ItemAddedToShoppingCart>((s, e) => s with { Items = s.Items.Add(new ShoppingCartItem(e.Id, e.Item)) });
    }

    public Guid Id { get; private init; }
    public ImmutableList<ShoppingCartItem> Items { get; private init; } = ImmutableList<ShoppingCartItem>.Empty;
    public string? NewField { get; private init; }
}