using System.Linq.Expressions;
using System.Reflection;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Tests.Shared;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Unit.Filtering;

public class PredicateWeakeningTests
{
    private static readonly Customer Ann = new() { Name = "Ann" };
    private static readonly Customer? Nobody = null;
    private static int _limit;

    [Test]
    public void Weaken_WhenPredicateOrdersMember_RulesOutNumbersOnWrongSide()
    {
        Weaken(e => e.Total > 100).ShouldBe(!Stored("Total").LessThanOrEqualTo(100));
        Weaken(e => e.Total >= 100).ShouldBe(!Stored("Total").LessThan(100));
        Weaken(e => e.Total < 100).ShouldBe(!Stored("Total").GreaterThanOrEqualTo(100));
        Weaken(e => e.Total <= 100).ShouldBe(!Stored("Total").GreaterThan(100));
    }

    [Test]
    public void Weaken_WhenPredicateEqualsMember_RulesOutValuesThatDiffer()
    {
        Weaken(e => e.Total == 100).ShouldBe(!Stored("Total").NotEqualTo(100));
        Weaken(e => e.Note == "fragile").ShouldBe(!Stored("Note").NotEqualTo("fragile"));

        // Written out, as that is how a predicate may come.
#pragma warning disable IDE0100
        Weaken(e => e.IsGift == true).ShouldBe(!Stored("IsGift").EqualTo(false));
#pragma warning restore IDE0100
    }

    [Test]
    public void Weaken_WhenPredicateDiffersFromMember_RulesOutMatchingValues()
    {
        Weaken(e => e.Total != 100).ShouldBe(!Stored("Total").EqualTo(100));
        Weaken(e => e.Note != "fragile").ShouldBe(!Stored("Note").EqualTo("fragile"));
    }

    [Test]
    public void Weaken_WhenPredicateIsBooleanMember_RulesOutOppositeValues()
    {
        Weaken(e => e.IsGift).ShouldBe(!Stored("IsGift").EqualTo(false));
        Weaken(e => !e.IsGift).ShouldBe(!Stored("IsGift").EqualTo(true));
    }

    [Test]
    public void Weaken_WhenValueComesFirst_UsesConverse()
    {
        Weaken(e => 100 < e.Total).ShouldBe(Weaken(e => e.Total > 100));
        Weaken(e => 100 >= e.Total).ShouldBe(Weaken(e => e.Total <= 100));
        Weaken(e => "fragile" == e.Note).ShouldBe(Weaken(e => e.Note == "fragile"));
    }

    [Test]
    public void Weaken_WhenPredicateIsNegated_RulesOutOppositeValues()
    {
        Weaken(e => !(e.Total > 100)).ShouldBe(!Stored("Total").GreaterThan(100));
        Weaken(e => !(e.Note == "fragile")).ShouldBe(!Stored("Note").EqualTo("fragile"));
        Weaken(e => !!(e.Total > 100)).ShouldBe(Weaken(e => e.Total > 100));
    }

    [Test]
    public void Weaken_WhenPredicateIsCombined_CombinesRules()
    {
        var total = !Stored("Total").LessThanOrEqualTo(100);
        var gift = !Stored("IsGift").EqualTo(false);

        Weaken(e => e.Total > 100 && e.IsGift).ShouldBe(total & gift);
        Weaken(e => e.Total > 100 || e.IsGift).ShouldBe(total | gift);
        Weaken(e => e.Total > 100 & e.IsGift).ShouldBe(total & gift);
        Weaken(e => e.Total > 100 | e.IsGift).ShouldBe(total | gift);
    }

    [Test]
    public void Weaken_WhenCombinedPredicateIsNegated_AppliesDeMorgan()
    {
        var total = !Stored("Total").GreaterThan(100);
        var gift = !Stored("IsGift").EqualTo(true);

        Weaken(e => !(e.Total > 100 && e.IsGift)).ShouldBe(total | gift);
        Weaken(e => !(e.Total > 100 || e.IsGift)).ShouldBe(total & gift);
    }

    [Test]
    public void Weaken_WhenSideCannotBeLookedUp_KeepsOnlyWhatIsSafe()
    {
        Weaken(e => e.Total > 100 && e.Note!.StartsWith("fr")).ShouldBe(!Stored("Total").LessThanOrEqualTo(100));
        Weaken(e => e.Total > 100 || e.Note!.StartsWith("fr")).ShouldBe(EventFilter.All);
        Weaken(e => !(e.Total > 100 && e.Note!.StartsWith("fr"))).ShouldBe(EventFilter.All);
        Weaken(e => !(e.Total > 100 || e.Note!.StartsWith("fr"))).ShouldBe(!Stored("Total").GreaterThan(100));
    }

    [Test]
    public void Weaken_WhenMemberIsNested_UsesNestedPath()
    {
        Weaken(e => e.Customer!.Name == "Ann").ShouldBe(!Stored("Customer.Name").NotEqualTo("Ann"));
    }

    [Test]
    public void Weaken_WhenMemberIsNullable_WeakensUnderlyingValue()
    {
        Weaken(e => e.Discount > 5).ShouldBe(!Stored("Discount").LessThanOrEqualTo(5));
        Weaken(e => e.Discount != 7).ShouldBe(!Stored("Discount").EqualTo(7));
    }

    [Test]
    public void Weaken_WhenMemberIsEnum_WeakensToUnderlyingNumber()
    {
        Weaken(e => e.Size > Size.Small).ShouldBe(!Stored("Size").LessThanOrEqualTo(0));
        Weaken(e => e.Size == Size.Medium).ShouldBe(!Stored("Size").NotEqualTo(1));
        Weaken(e => e.Size == Size.Large).ShouldBe(!Stored("Size").NotEqualTo(2));
    }

    [Test]
    public void Weaken_WhenMemberIsLosslesslyWidened_WeakensUsingWidenedValue()
    {
        Weaken(e => e.Total < 50L).ShouldBe(!Stored("Total").GreaterThanOrEqualTo(50));
        Weaken(e => e.Total > 99.5m).ShouldBe(!Stored("Total").LessThanOrEqualTo(99.5m));
        Weaken(e => e.Price >= 10.99m).ShouldBe(!Stored("Price").LessThan(10.99m));
        Weaken(e => e.Serial < long.MaxValue).ShouldBe(!Stored("Serial").GreaterThanOrEqualTo(long.MaxValue));
    }

    [Test]
    public void Weaken_WhenValueIsCaptured_WeakensWithCapturedValue()
    {
        // ReSharper disable once ConvertToConstant.Local - capturing a local intentionally
        var limit = 100;

        Weaken(e => e.Total > limit).ShouldBe(!Stored("Total").LessThanOrEqualTo(100));
        Weaken(e => e.Note == Ann.Name).ShouldBe(!Stored("Note").NotEqualTo("Ann"));
        Weaken(e => e.Total > Ann.Name!.Length).ShouldBe(!Stored("Total").LessThanOrEqualTo(3));
    }

    [Test]
    public void Weaken_WhenValueCannotBeEvaluated_RulesNothingOut()
    {
        Weaken(e => e.Note == Nobody!.Name).ShouldBe(EventFilter.All);
        Weaken(e => e.Total > e.Items).ShouldBe(EventFilter.All);
        Weaken(e => e.Total > Math.Abs(-100)).ShouldBe(EventFilter.All);
    }

    [Test]
    public void Weaken_WhenValueCanChange_RulesNothingOut()
    {
        Weaken(e => e.Serial < DateTime.UtcNow.Ticks).ShouldBe(EventFilter.All);
        Weaken(e => e.Total > Environment.TickCount).ShouldBe(EventFilter.All);

        _limit = 100;

        Weaken(e => e.Total > _limit).ShouldBe(EventFilter.All);
    }

    [Test]
    public void Weaken_WhenValueIsNull_RulesNothingOut()
    {
        // Whether a stored null deserializes as null is for the serializer to determine.
        Weaken(e => e.Note == null).ShouldBe(EventFilter.All);
        Weaken(e => e.Discount == null).ShouldBe(EventFilter.All);
        Weaken(e => e.Note != Ann.Nickname).ShouldBe(EventFilter.All);
    }

    [Test]
    public void Weaken_WhenStoredNumberMayCompareDifferently_RulesNothingOut()
    {
        // The stored number may not compare the same way after deserialization.
        Weaken(e => e.Weight <= 0.3).ShouldBe(EventFilter.All);
        Weaken(e => e.Total > 99.5).ShouldBe(EventFilter.All);

        // A narrowing conversion may change the value seen by the predicate.
        Weaken(e => (int)e.Price <= 5).ShouldBe(EventFilter.All);
        Weaken(e => (int)e.Serial > 5).ShouldBe(EventFilter.All);
    }

    [Test]
    public void Weaken_WhenMemberHasNoStoredPath_RulesNothingOut()
    {
        Weaken(e => e.Lines.Count > 2).ShouldBe(EventFilter.All);
        Weaken(e => e.Note!.Length > 3).ShouldBe(EventFilter.All);
        Weaken(e => e.Discount.HasValue).ShouldBe(EventFilter.All);
        Weaken(e => e.Tags.Length > 1).ShouldBe(EventFilter.All);
    }

    [Test]
    public void Weaken_WhenExpressionIsUnsupported_RulesNothingOut()
    {
        Weaken(e => e.Note!.StartsWith("fr")).ShouldBe(EventFilter.All);
        Weaken(e => e.IsGift ? e.Total > 100 : e.Items < 3).ShouldBe(EventFilter.All);
        Weaken(e => e.Customer == Ann).ShouldBe(EventFilter.All);
        Weaken(e => e.Total + 1 > 100).ShouldBe(EventFilter.All);
        Weaken(_ => true).ShouldBe(EventFilter.All);
    }

    private static StoredPayload Stored(string path) => StoredPayload.At(path);

    private static EventFilter Weaken(Expression<Func<Order, bool>> predicate) =>
        PredicateWeakening.Weaken(predicate, GetStoredPath);

    // This test serializer stores members of these test types under their names; members of other types, such as
    // string.Length, have no stored path.
    private static string? GetStoredPath(IReadOnlyList<MemberInfo> members) =>
        members.All(x => x.DeclaringType!.DeclaringType == typeof(PredicateWeakeningTests))
            ? string.Join('.', members.Select(x => x.Name))
            : null;

    private enum Size
    {
        Small,
        Medium,
        Large
    }

    private sealed class Order
    {
        public int Total { get; init; }

        public int Items { get; init; }

        public int? Discount { get; init; }

        public bool IsGift { get; init; }

        public string? Note { get; init; }

        public Customer? Customer { get; init; }

        public Size Size { get; init; }

        public decimal Price { get; init; }

        public double Weight { get; init; }

        public long Serial { get; init; }

        public List<Line> Lines { get; init; } = [];

        public string[] Tags { get; init; } = [];
    }

    private sealed class Customer
    {
        public string? Name { get; init; }

        public string? Nickname { get; init; }
    }

    private sealed class Line
    {
        public int Count { get; init; }
    }
}