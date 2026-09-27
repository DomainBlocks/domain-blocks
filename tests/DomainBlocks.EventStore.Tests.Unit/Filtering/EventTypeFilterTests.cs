using System.Linq.Expressions;
using DomainBlocks.EventStore.Filtering;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Unit.Filtering;

public class EventTypeFilterTests
{
    [Test]
    public void OfType_WhenMatched_MatchesTheTypeAndWhatDerivesFromIt()
    {
        EventFilter.OfType<OrderPlaced>().Matches(Stored(new OrderPlaced())).ShouldBeTrue();
        EventFilter.OfType<IOrderEvent>().Matches(Stored(new OrderPlaced())).ShouldBeTrue();
        EventFilter.OfType<OrderPlaced>().Matches(Stored(new RushOrderPlaced())).ShouldBeTrue();

        EventFilter.OfType<RushOrderPlaced>().Matches(Stored(new OrderPlaced())).ShouldBeFalse();
        EventFilter.OfType<IOrderEvent>().Matches(Stored(new InvoiceRaised())).ShouldBeFalse();
    }

    [Test]
    public void OfType_WhenGivenAPredicate_MatchesEventsItIsTrueOf()
    {
        var filter = EventFilter.OfType<OrderPlaced>(e => e.Total > 100);

        filter.Matches(Stored(new OrderPlaced { Total = 101 })).ShouldBeTrue();
        filter.Matches(Stored(new OrderPlaced { Total = 100 })).ShouldBeFalse();
    }

    [Test]
    public void OfType_WhenTheEventIsOfAnotherType_DoesNotEvaluateThePredicate()
    {
        var filter = EventFilter.OfType<OrderPlaced>(e => Fail());

        filter.Matches(Stored(new InvoiceRaised())).ShouldBeFalse();
    }

    [Test]
    public void OfType_WhenThePredicateMeetsANull_DoesNotMatch()
    {
        var filter = EventFilter.OfType<OrderPlaced>(e => e.Customer!.Name == "Ann");

        filter.Matches(Stored(new OrderPlaced { Customer = null })).ShouldBeFalse();
        (!filter).Matches(Stored(new OrderPlaced { Customer = null })).ShouldBeTrue();
    }

    [Test]
    public void OfType_WhenThePredicateThrowsAnythingElse_Throws()
    {
        var filter = EventFilter.OfType<OrderPlaced>(e => 1 / e.Total == 1);

        Should.Throw<DivideByZeroException>(() => filter.Matches(Stored(new OrderPlaced { Total = 0 })));
    }

    [Test]
    public void And_WhenACheaperOperandRulesAnEventOut_DoesNotDecodeIt()
    {
        // The predicate is written first, and still evaluated last.
        var filter = EventFilter.OfType<OrderPlaced>(e => e.Total > 100) &
                     EventFilter.Metadata("tenant", "acme") &
                     EventFilter.StreamId("order-1");

        var subject = new UndecodableEvent();

        filter.Matches(subject).ShouldBeFalse();
    }

    [Test]
    public void Equals_WhenPredicatesAreTheSameExpression_IsTrue()
    {
        Expression<Func<OrderPlaced, bool>> predicate = e => e.Total > 100;

        EventFilter.OfType(predicate).ShouldBe(EventFilter.OfType(predicate));
        EventFilter.OfType<OrderPlaced>().ShouldBe(EventFilter.OfType<OrderPlaced>());
    }

    [Test]
    public void Equals_WhenPredicatesAreOnlyEquivalent_IsFalse()
    {
        EventFilter.OfType<OrderPlaced>(e => e.Total > 100)
            .ShouldNotBe(EventFilter.OfType<OrderPlaced>(e => e.Total > 100));

        EventFilter.OfType<OrderPlaced>().ShouldNotBe(EventFilter.OfType<OrderPlaced>(e => true));
        EventFilter.OfType<OrderPlaced>().ShouldNotBe(EventFilter.OfType<InvoiceRaised>());
    }

    [Test]
    public void ToString_WhenWritten_NamesTheTypeWithoutItsAssembly()
    {
        var typeName = typeof(OrderPlaced).ToString();

        EventFilter.OfType<OrderPlaced>().ToString().ShouldBe($"""eventType("{typeName}")""");

        EventFilter.OfType<OrderPlaced>(e => e.Total > 100).ToString()
            .ShouldBe($"""eventType("{typeName}","e => (e.Total > 100)")""");

        EventFilter.OfType<List<OrderPlaced>>().ToString().ShouldNotContain("Version=");
    }

    private static StoredEvent Stored(object e) => new() { DecodedPayload = e };

    private static bool Fail() => throw new InvalidOperationException("The predicate was evaluated.");

    private interface IOrderEvent;

    private class OrderPlaced : IOrderEvent
    {
        public int Total { get; init; }

        public Customer? Customer { get; init; } = new("Ann");
    }

    private sealed class RushOrderPlaced : OrderPlaced;

    private sealed class InvoiceRaised;

    private sealed record Customer(string Name);

    private sealed class UndecodableEvent : IFilterableEvent
    {
        public string EventName => "OrderPlaced";

        public string StreamId => "order-2";

        public DateTimeOffset CreatedAt => default;

        public object DecodedPayload => throw new InvalidOperationException("The event was decoded.");

        public bool TryGetMetadata(string key, out string value)
        {
            value = "acme";
            return true;
        }
    }
}