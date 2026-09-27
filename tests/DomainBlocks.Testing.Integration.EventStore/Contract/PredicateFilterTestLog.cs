using System.Linq.Expressions;
using DomainBlocks.EventStore;
using DomainBlocks.EventStore.TypeMapping;

namespace DomainBlocks.Testing.Integration.EventStore.Contract;

/// <summary>
/// A basket as it was first stored, before it had anything but a total.
/// </summary>
public sealed record BasketAsFirstStored
{
    public required int Total { get; init; }
}

public sealed record Basket
{
    public int Total { get; init; }

    public int Items { get; init; }

    public int? Discount { get; init; }

    public bool IsGift { get; init; }

    public string? Note { get; init; }

    public BasketOwner? Owner { get; init; }

    public BasketSize Size { get; init; }

    public decimal Price { get; init; }

    public double Weight { get; init; }

    public long Serial { get; init; }

    public List<BasketLine> Lines { get; init; } = [];
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

/// <summary>
/// A line has a count of its own, which is not the count of the lines.
/// </summary>
public sealed record BasketLine
{
    public int Count { get; init; }
}

public sealed record PredicateFilterCase(string Name, Expression<Func<Basket, bool>> Predicate)
{
    public Func<Basket, bool> Compiled { get; } = Predicate.Compile();

    public override string ToString() => Name;
}

/// <summary>
/// A log of one event under two shapes, and predicates over it. A predicate is about the event as it is read, so what
/// it selects must not depend on what a store makes of it for its database. The older shape lacks most members, which
/// are then read as their defaults, and the newer has nulls, so that a member is often not stored as what it is read
/// as.
/// </summary>
public static class PredicateFilterTestLog
{
    public const string BasketEventName = "Basket";

    // Not a constant, so that a predicate has to go and get it.
    private static readonly int Limit = int.Parse("100");

    private static readonly BasketOwner Ann = new() { Name = "Ann" };

    /// <summary>
    /// Writes baskets as they were first stored.
    /// </summary>
    public static EventTypeMap FirstTypeMap { get; } = EventTypeMap.Create(
        EventTypeMapping.ReadWrite<BasketAsFirstStored>(BasketEventName),
        EventTypeMapping.ReadWrite<InvoiceRaised>());

    public static EventTypeMap TypeMap { get; } = EventTypeMap.Create(
        EventTypeMapping.ReadWrite<Basket>(BasketEventName),
        EventTypeMapping.ReadWrite<InvoiceRaised>());

    public static IEnumerable<AppendableEvent<object>> FirstEvents()
    {
        foreach (var total in (int[])[5, 15, 150, 1000])
            yield return AppendableEvent.Create<object>(new BasketAsFirstStored { Total = total });

        yield return AppendableEvent.Create<object>(new InvoiceRaised { Amount = 1 });
    }

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
                Size = (BasketSize)(i % 3),
                Price = i + 0.99m,
                Weight = i * 0.1,
                Serial = long.MaxValue - i,
                Lines = [.. Enumerable.Repeat(new BasketLine { Count = 1 }, i % 4)]
            });
        }

        yield return AppendableEvent.Create<object>(new InvoiceRaised { Amount = 2 });
    }

    public static IReadOnlyList<PredicateFilterCase> Cases { get; } =
    [
        new("GreaterThan", e => e.Total > 100),
        new("GreaterThan_Mirrored", e => 100 < e.Total),
        new("GreaterThan_AStaticField", e => e.Total > Limit),
        new("GreaterThan_AValueFromAnObject", e => e.Note == Ann.Name || e.Total > Ann.Name!.Length),
        new("Not_GreaterThan", e => !(e.Total > 100)),

        // A member that was not stored is read as its default.
        new("LessThan_MemberNotStored", e => e.Items < 3),
        new("Equal_MemberNotStored", e => e.Items == 0),
        new("NotEqual_MemberNotStored", e => e.Items != 1),
        new("Boolean_MemberNotStored", e => !e.IsGift),
        new("Enum_MemberNotStored", e => e.Size == BasketSize.Small),
        new("Enum_NotEqual", e => e.Size != BasketSize.Large),
        new("Enum_GreaterThan", e => e.Size > BasketSize.Small),

        // Nothing is greater than a null, and a null is not equal to anything but a null.
        new("Nullable_GreaterThan", e => e.Discount > 5),
        new("Nullable_Not_GreaterThan", e => !(e.Discount > 5)),
        new("Nullable_NotEqual", e => e.Discount != 7),
        new("Nullable_Equal", e => e.Discount == 7),
        new("Nullable_IsNull", e => e.Discount == null),
        new("Nullable_HasValue", e => e.Discount.HasValue),
        new("Nullable_ValueOrDefault", e => e.Discount.GetValueOrDefault() > 5),

        new("Boolean", e => e.IsGift),
        // Written out, as that is how a predicate may come.
#pragma warning disable IDE0100
        new("Boolean_Equal", e => e.IsGift == false),
#pragma warning restore IDE0100

        new("Text_Equal", e => e.Note == "fragile"),
        new("Text_NotEqual", e => e.Note != "fragile"),
        new("Text_Not_Equal", e => !(e.Note == "fragile")),
        new("Text_IsNull", e => e.Note == null),
        new("Text_Method", e => e.Note!.StartsWith("fr")),
        new("Text_Length", e => e.Note!.Length > 0),

        // Without an owner the predicate meets a null, and so does not match, negated or not.
        new("Nested", e => e.Owner!.Name == "Ann"),
        new("Nested_Not", e => !(e.Owner!.Name == "Ann")),
        new("Nested_NotEqual", e => e.Owner!.Name != "Ann"),

        new("Decimal", e => e.Price >= 10.99m),
        new("Decimal_FromAnInteger", e => e.Total > 99.5m),
        new("Double", e => e.Weight <= 0.3),
        new("Long", e => e.Serial < long.MaxValue - 10),
        new("Long_FromAnInteger", e => e.Total < 50L),
        new("Cast_ThatLosesSomething", e => (int)e.Price <= 5),

        // The count of the lines, where each line has a count too.
        new("Count_OfAList", e => e.Lines.Count > 2),

        new("And", e => e.Total >= 10 && e.Items < 3),
        new("Or", e => e.Total > 200 || e.IsGift),
        new("Not_And", e => !(e.Total >= 10 && e.Items < 3)),
        new("Not_Or", e => !(e.Total > 200 || e.IsGift)),
        new("And_WithWhatCannotBeLookedUp", e => e.Total > 100 && e.Note!.StartsWith("fr")),
        new("Or_WithWhatCannotBeLookedUp", e => e.Total > 100 || e.Note!.StartsWith("fr")),
        new("Conditional", e => e.IsGift ? e.Total > 100 : e.Items < 3)
    ];
}