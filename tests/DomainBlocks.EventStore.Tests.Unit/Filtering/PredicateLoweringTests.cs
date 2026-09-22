using System.Linq.Expressions;
using System.Reflection;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Tests.Shared;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Unit.Filtering;

public class PredicateLoweringTests
{
    private static readonly Customer Ann = new() { Name = "Ann" };
    private static readonly Customer? Nobody = null;
    private static int _limit;

    [Test]
    public void Lower_WhenPredicateOrdersMember_RulesOutNumbersOnWrongSide()
    {
        Lower(e => e.Total > 100).ShouldBe(!Stored("Total").LessThanOrEqualTo(100));
        Lower(e => e.Total >= 100).ShouldBe(!Stored("Total").LessThan(100));
        Lower(e => e.Total < 100).ShouldBe(!Stored("Total").GreaterThanOrEqualTo(100));
        Lower(e => e.Total <= 100).ShouldBe(!Stored("Total").GreaterThan(100));
    }

    [Test]
    public void Lower_WhenPredicateEqualsMember_RulesOutValuesThatDiffer()
    {
        Lower(e => e.Total == 100).ShouldBe(!Stored("Total").NotEqualTo(100));
        Lower(e => e.Note == "fragile").ShouldBe(!Stored("Note").NotEqualTo("fragile"));

        // Written out, as that is how a predicate may come.
#pragma warning disable IDE0100
        Lower(e => e.IsGift == true).ShouldBe(!Stored("IsGift").EqualTo(false));
#pragma warning restore IDE0100
    }

    [Test]
    public void Lower_WhenPredicateDiffersFromMember_RulesOutMatchingValues()
    {
        Lower(e => e.Total != 100).ShouldBe(!Stored("Total").EqualTo(100));
        Lower(e => e.Note != "fragile").ShouldBe(!Stored("Note").EqualTo("fragile"));
    }

    [Test]
    public void Lower_WhenPredicateIsBooleanMember_RulesOutOppositeValues()
    {
        Lower(e => e.IsGift).ShouldBe(!Stored("IsGift").EqualTo(false));
        Lower(e => !e.IsGift).ShouldBe(!Stored("IsGift").EqualTo(true));
    }

    [Test]
    public void Lower_WhenValueComesFirst_UsesConverse()
    {
        Lower(e => 100 < e.Total).ShouldBe(Lower(e => e.Total > 100));
        Lower(e => 100 >= e.Total).ShouldBe(Lower(e => e.Total <= 100));
        Lower(e => "fragile" == e.Note).ShouldBe(Lower(e => e.Note == "fragile"));
    }

    [Test]
    public void Lower_WhenPredicateIsNegated_RulesOutOppositeValues()
    {
        Lower(e => !(e.Total > 100)).ShouldBe(!Stored("Total").GreaterThan(100));
        Lower(e => !(e.Note == "fragile")).ShouldBe(!Stored("Note").EqualTo("fragile"));
        Lower(e => !!(e.Total > 100)).ShouldBe(Lower(e => e.Total > 100));
    }

    [Test]
    public void Lower_WhenPredicateIsCombined_CombinesRules()
    {
        var total = !Stored("Total").LessThanOrEqualTo(100);
        var gift = !Stored("IsGift").EqualTo(false);

        Lower(e => e.Total > 100 && e.IsGift).ShouldBe(total & gift);
        Lower(e => e.Total > 100 || e.IsGift).ShouldBe(total | gift);
        Lower(e => e.Total > 100 & e.IsGift).ShouldBe(total & gift);
        Lower(e => e.Total > 100 | e.IsGift).ShouldBe(total | gift);
    }

    [Test]
    public void Lower_WhenCombinedPredicateIsNegated_AppliesDeMorgan()
    {
        var total = !Stored("Total").GreaterThan(100);
        var gift = !Stored("IsGift").EqualTo(true);

        Lower(e => !(e.Total > 100 && e.IsGift)).ShouldBe(total | gift);
        Lower(e => !(e.Total > 100 || e.IsGift)).ShouldBe(total & gift);
    }

    [Test]
    public void Lower_WhenSideCannotBeLookedUp_KeepsOnlyWhatIsSafe()
    {
        Lower(e => e.Total > 100 && e.Note!.StartsWith("fr")).ShouldBe(!Stored("Total").LessThanOrEqualTo(100));
        Lower(e => e.Total > 100 || e.Note!.StartsWith("fr")).ShouldBe(EventFilter.All);
        Lower(e => !(e.Total > 100 && e.Note!.StartsWith("fr"))).ShouldBe(EventFilter.All);
        Lower(e => !(e.Total > 100 || e.Note!.StartsWith("fr"))).ShouldBe(!Stored("Total").GreaterThan(100));
    }

    [Test]
    public void Lower_WhenMemberIsNested_UsesNestedPath()
    {
        Lower(e => e.Customer!.Name == "Ann").ShouldBe(!Stored("Customer.Name").NotEqualTo("Ann"));
    }

    [Test]
    public void Lower_WhenMemberIsNullable_LowersUnderlyingValue()
    {
        Lower(e => e.Discount > 5).ShouldBe(!Stored("Discount").LessThanOrEqualTo(5));
        Lower(e => e.Discount != 7).ShouldBe(!Stored("Discount").EqualTo(7));
    }

    [Test]
    public void Lower_WhenMemberIsEnum_LowersToUnderlyingNumber()
    {
        Lower(e => e.Size > Size.Small).ShouldBe(!Stored("Size").LessThanOrEqualTo(0));
        Lower(e => e.Size == Size.Medium).ShouldBe(!Stored("Size").NotEqualTo(1));
        Lower(e => e.Size == Size.Large).ShouldBe(!Stored("Size").NotEqualTo(2));
    }

    [Test]
    public void Lower_WhenMemberIsLosslesslyWidened_LowersUsingWidenedValue()
    {
        Lower(e => e.Total < 50L).ShouldBe(!Stored("Total").GreaterThanOrEqualTo(50));
        Lower(e => e.Total > 99.5m).ShouldBe(!Stored("Total").LessThanOrEqualTo(99.5m));
        Lower(e => e.Price >= 10.99m).ShouldBe(!Stored("Price").LessThan(10.99m));
        Lower(e => e.Serial < long.MaxValue).ShouldBe(!Stored("Serial").GreaterThanOrEqualTo(long.MaxValue));
    }

    [Test]
    public void Lower_WhenValueIsCaptured_LowersCapturedValue()
    {
        // ReSharper disable once ConvertToConstant.Local - capturing a local intentionally
        var limit = 100;

        Lower(e => e.Total > limit).ShouldBe(!Stored("Total").LessThanOrEqualTo(100));
        Lower(e => e.Note == Ann.Name).ShouldBe(!Stored("Note").NotEqualTo("Ann"));
        Lower(e => e.Total > Ann.Name!.Length).ShouldBe(!Stored("Total").LessThanOrEqualTo(3));
    }

    [Test]
    public void Lower_WhenValueCannotBeEvaluated_RulesNothingOut()
    {
        Lower(e => e.Note == Nobody!.Name).ShouldBe(EventFilter.All);
        Lower(e => e.Total > e.Items).ShouldBe(EventFilter.All);
        Lower(e => e.Total > Math.Abs(-100)).ShouldBe(EventFilter.All);
    }

    [Test]
    public void Lower_WhenValueCanChange_RulesNothingOut()
    {
        Lower(e => e.Serial < DateTime.UtcNow.Ticks).ShouldBe(EventFilter.All);
        Lower(e => e.Total > Environment.TickCount).ShouldBe(EventFilter.All);

        _limit = 100;

        Lower(e => e.Total > _limit).ShouldBe(EventFilter.All);
    }

    [Test]
    public void Lower_WhenValueIsNull_RulesNothingOut()
    {
        // Whether a stored null deserializes as null is for the serializer to determine.
        Lower(e => e.Note == null).ShouldBe(EventFilter.All);
        Lower(e => e.Discount == null).ShouldBe(EventFilter.All);
        Lower(e => e.Note != Ann.Nickname).ShouldBe(EventFilter.All);
    }

    [Test]
    public void Lower_WhenStoredNumberMayCompareDifferently_RulesNothingOut()
    {
        // The stored number may not compare the same way after deserialization.
        Lower(e => e.Weight <= 0.3).ShouldBe(EventFilter.All);
        Lower(e => e.Total > 99.5).ShouldBe(EventFilter.All);

        // A narrowing conversion may change the value seen by the predicate.
        Lower(e => (int)e.Price <= 5).ShouldBe(EventFilter.All);
        Lower(e => (int)e.Serial > 5).ShouldBe(EventFilter.All);
    }

    [Test]
    public void Lower_WhenMemberHasNoStoredPath_RulesNothingOut()
    {
        Lower(e => e.Lines.Count > 2).ShouldBe(EventFilter.All);
        Lower(e => e.Note!.Length > 3).ShouldBe(EventFilter.All);
        Lower(e => e.Discount.HasValue).ShouldBe(EventFilter.All);
        Lower(e => e.Tags.Length > 1).ShouldBe(EventFilter.All);
    }

    [Test]
    public void Lower_WhenExpressionIsUnsupported_RulesNothingOut()
    {
        Lower(e => e.Note!.StartsWith("fr")).ShouldBe(EventFilter.All);
        Lower(e => e.IsGift ? e.Total > 100 : e.Items < 3).ShouldBe(EventFilter.All);
        Lower(e => e.Customer == Ann).ShouldBe(EventFilter.All);
        Lower(e => e.Total + 1 > 100).ShouldBe(EventFilter.All);
        Lower(_ => true).ShouldBe(EventFilter.All);
    }

    private static StoredPayload Stored(string path) => StoredPayload.At(path);

    private static EventFilter Lower(Expression<Func<Order, bool>> predicate) =>
        PredicateLowering.Lower(predicate, GetStoredPath);

    // This test serializer stores members of these test types under their names; members of other types, such as
    // string.Length, have no stored path.
    private static string? GetStoredPath(IReadOnlyList<MemberInfo> members) =>
        members.All(x => x.DeclaringType!.DeclaringType == typeof(PredicateLoweringTests))
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