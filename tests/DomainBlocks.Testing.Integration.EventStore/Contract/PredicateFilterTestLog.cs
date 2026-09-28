using System.Linq.Expressions;
using DomainBlocks.EventStore;
using DomainBlocks.EventStore.TypeMapping;

namespace DomainBlocks.Testing.Integration.EventStore.Contract;

public sealed record Basket
{
    public int Total { get; init; }

    public int Items { get; init; }

    public int? Discount { get; init; }

    public bool IsGift { get; init; }

    public string? Note { get; init; }

    public BasketOwner? Owner { get; init; }

    public BasketSize Size { get; init; }
}

public sealed record BasketOwner
{
    public string? Name { get; init; }
}

public enum BasketSize
{
    Small,
    Medium,
    Large
}

public sealed record PredicateFilterCase(string Name, Expression<Func<Basket, bool>> Predicate)
{
    public Func<Basket, bool> Compiled { get; } = Predicate.Compile();

    public override string ToString() => Name;
}

/// <summary>
/// A log of baskets, and predicates over them. A predicate is about the event as it is read, so what it selects must
/// not depend on how much of a filter a store pushes down to its database.
/// </summary>
public static class PredicateFilterTestLog
{
    public const string BasketEventName = "Basket";

    public static EventTypeMap TypeMap { get; } = EventTypeMap.Create(
        EventTypeMapping.ReadWrite<Basket>(BasketEventName),
        EventTypeMapping.ReadWrite<InvoiceRaised>());

    public static IEnumerable<AppendableEvent<object>> Events()
    {
        for (var i = 0; i < 24; i++)
        {
            yield return AppendableEvent.Create<object>(new Basket
            {
                Total = i * 10,
                Items = i % 5,
                Discount = i % 3 == 0 ? null : i,
                IsGift = i % 2 == 0,
                Note = (i % 4) switch { 0 => null, 1 => "fragile", 2 => "Fragile", _ => "" },
                Owner = (i % 3) switch { 0 => null, 1 => new BasketOwner { Name = "Ann" }, _ => new BasketOwner() },
                Size = (BasketSize)(i % 3)
            });
        }

        yield return AppendableEvent.Create<object>(new InvoiceRaised { Amount = 2 });
    }

    public static IReadOnlyList<PredicateFilterCase> Cases { get; } =
    [
        new("GreaterThan", e => e.Total > 100),
        new("Not_GreaterThan", e => !(e.Total > 100)),
        new("Enum_NotEqual", e => e.Size != BasketSize.Large),

        // Nothing is greater than a null, and a null is not equal to anything but a null.
        new("Nullable_GreaterThan", e => e.Discount > 5),
        new("Nullable_IsNull", e => e.Discount == null),

        new("Boolean", e => e.IsGift),
        new("Text_Equal", e => e.Note == "fragile"),
        new("Text_Method", e => e.Note!.StartsWith("fr")),

        // Without an owner the predicate meets a null, and so does not match.
        new("Nested", e => e.Owner!.Name == "Ann"),

        new("And", e => e.Total >= 10 && e.Items < 3),
        new("Or", e => e.Total > 200 || e.IsGift),
        new("Conditional", e => e.IsGift ? e.Total > 100 : e.Items < 3)
    ];
}
