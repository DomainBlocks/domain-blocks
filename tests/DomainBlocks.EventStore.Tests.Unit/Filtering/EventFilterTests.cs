using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Filtering.Nodes;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Unit.Filtering;

public class EventFilterTests
{
    private static readonly DateTimeOffset Noon = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void All_AnyEvent_Matches()
    {
        EventFilter.All.Matches(new TestFilterableEvent()).ShouldBeTrue();
    }

    [Test]
    public void None_AnyEvent_DoesNotMatch()
    {
        EventFilter.None.Matches(new TestFilterableEvent()).ShouldBeFalse();
    }

    [TestCase("OrderPlaced", true)]
    [TestCase("OrderShipped", true)]
    [TestCase("OrderCancelled", false)]
    [TestCase("orderplaced", false)]
    [TestCase("OrderPlaced ", false)]
    public void EventNames_EventWithName_MatchesOnlyGivenNames(string eventName, bool expected)
    {
        var filter = EventFilter.EventNames("OrderPlaced", "OrderShipped");

        filter.Matches(new TestFilterableEvent { EventName = eventName }).ShouldBe(expected);
    }

    [Test]
    public void EventNames_DuplicateNamesInAnyOrder_KeepsDistinctNamesInOrdinalOrder()
    {
        var filter = EventFilter.EventNames("b", "a", "B", "b");

        filter.ShouldBeOfType<EventNameFilter>().Names.ShouldBe(["B", "a", "b"]);
    }

    [Test]
    public void EventNames_NoNames_ReturnsNone()
    {
        EventFilter.EventNames().ShouldBeSameAs(EventFilter.None);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("Order\0Placed")]
    public void EventNames_InvalidName_Throws(string? eventName)
    {
        Should.Throw<ArgumentException>(() => EventFilter.EventNames("OrderPlaced", eventName!));
    }

    [Test]
    public void EventNames_NullNames_Throws()
    {
        Should.Throw<ArgumentNullException>(() => EventFilter.EventNames(null!));
    }

    [TestCase("order-1", true)]
    [TestCase("order-2", true)]
    [TestCase("order-10", false)]
    [TestCase("Order-1", false)]
    public void StreamIds_EventInStream_MatchesOnlyGivenStreams(string streamId, bool expected)
    {
        var filter = EventFilter.StreamIds("order-1", "order-2");

        filter.Matches(new TestFilterableEvent { StreamId = streamId }).ShouldBe(expected);
    }

    [Test]
    public void StreamIds_DuplicateIdsInAnyOrder_KeepsDistinctIdsInOrdinalOrder()
    {
        var filter = EventFilter.StreamIds("order-2", "order-1", "order-2");

        filter.ShouldBeOfType<StreamIdFilter>().Ids.ShouldBe(["order-1", "order-2"]);
    }

    [Test]
    public void StreamIds_NoIds_ReturnsNone()
    {
        EventFilter.StreamIds().ShouldBeSameAs(EventFilter.None);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("order\0-1")]
    public void StreamIds_InvalidId_Throws(string? streamId)
    {
        Should.Throw<ArgumentException>(() => EventFilter.StreamIds("order-1", streamId!));
    }

    [Test]
    public void StreamIds_NullIds_Throws()
    {
        Should.Throw<ArgumentNullException>(() => EventFilter.StreamIds(null!));
    }

    [TestCase("order-1", true)]
    [TestCase("order-", true)]
    [TestCase("order-10", true)]
    [TestCase("order", false)]
    [TestCase("Order-1", false)]
    [TestCase("my-order-1", false)]
    public void StreamIdStartsWith_EventInStream_MatchesOnlyIdsWithPrefix(string streamId, bool expected)
    {
        var filter = EventFilter.StreamIdStartsWith("order-");

        filter.Matches(new TestFilterableEvent { StreamId = streamId }).ShouldBe(expected);
    }

    [Test]
    public void StreamIdStartsWith_Prefix_ReturnsPrefixFilter()
    {
        var filter = EventFilter.StreamIdStartsWith("order-");

        filter.ShouldBeOfType<StreamIdPrefixFilter>().Prefix.ShouldBe("order-");
    }

    [Test]
    public void StreamIdStartsWith_EmptyPrefix_ReturnsAll()
    {
        EventFilter.StreamIdStartsWith("").ShouldBeSameAs(EventFilter.All);
    }

    [TestCase(null)]
    [TestCase("order\0-")]
    public void StreamIdStartsWith_InvalidPrefix_Throws(string? prefix)
    {
        Should.Throw<ArgumentException>(() => EventFilter.StreamIdStartsWith(prefix!));
    }

    [Test]
    public void MetadataExists_EventWithKey_Matches()
    {
        var filter = EventFilter.MetadataExists("tenant");
        var e = new TestFilterableEvent { Metadata = { ["tenant"] = "" } };

        filter.Matches(e).ShouldBeTrue();
        filter.ShouldBeOfType<MetadataExistsFilter>().Key.ShouldBe("tenant");
    }

    [Test]
    public void MetadataExists_EventWithoutKey_DoesNotMatch()
    {
        var filter = EventFilter.MetadataExists("tenant");
        var e = new TestFilterableEvent { Metadata = { ["region"] = "eu" } };

        filter.Matches(e).ShouldBeFalse();
        filter.Matches(new TestFilterableEvent()).ShouldBeFalse();
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("ten\0ant")]
    public void MetadataExists_InvalidKey_Throws(string? key)
    {
        Should.Throw<ArgumentException>(() => EventFilter.MetadataExists(key!));
    }

    [TestCase("acme", true)]
    [TestCase("initech", true)]
    [TestCase("", true)]
    [TestCase("globex", false)]
    [TestCase("Acme", false)]
    public void Metadata_EventWithValue_MatchesOnlyGivenValues(string value, bool expected)
    {
        var filter = EventFilter.Metadata("tenant", "acme", "initech", "");
        var e = new TestFilterableEvent { Metadata = { ["tenant"] = value } };

        filter.Matches(e).ShouldBe(expected);
    }

    [Test]
    public void Metadata_EventWithoutKey_DoesNotMatch()
    {
        var filter = EventFilter.Metadata("tenant", "acme");
        var e = new TestFilterableEvent { Metadata = { ["region"] = "acme" } };

        filter.Matches(e).ShouldBeFalse();
        filter.Matches(new TestFilterableEvent()).ShouldBeFalse();
    }

    [Test]
    public void Metadata_DuplicateValuesInAnyOrder_KeepsDistinctValuesInOrdinalOrder()
    {
        var filter = EventFilter.Metadata("tenant", "initech", "acme", "initech").ShouldBeOfType<MetadataValueFilter>();

        filter.Key.ShouldBe("tenant");
        filter.Values.ShouldBe(["acme", "initech"]);
    }

    [Test]
    public void Metadata_NoValues_ReturnsNone()
    {
        EventFilter.Metadata("tenant").ShouldBeSameAs(EventFilter.None);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("ten\0ant")]
    public void Metadata_InvalidKey_Throws(string? key)
    {
        Should.Throw<ArgumentException>(() => EventFilter.Metadata(key!, "acme"));
    }

    [TestCase(null)]
    [TestCase("ac\0me")]
    public void Metadata_InvalidValue_Throws(string? value)
    {
        Should.Throw<ArgumentException>(() => EventFilter.Metadata("tenant", "acme", value!));
    }

    [Test]
    public void Metadata_NullValues_Throws()
    {
        Should.Throw<ArgumentNullException>(() => EventFilter.Metadata("tenant", null!));
    }

    [TestCase(-1, false)]
    [TestCase(0, true)]
    [TestCase(1, true)]
    public void CreatedAtOrAfter_EventCreatedAroundBound_MatchesFromBound(long ticksFromBound, bool expected)
    {
        var filter = EventFilter.CreatedAtOrAfter(Noon);

        filter.Matches(new TestFilterableEvent { CreatedAt = Noon.AddTicks(ticksFromBound) }).ShouldBe(expected);
    }

    [TestCase(-1, true)]
    [TestCase(0, false)]
    [TestCase(1, false)]
    public void CreatedBefore_EventCreatedAroundBound_MatchesUpToBound(long ticksFromBound, bool expected)
    {
        var filter = EventFilter.CreatedBefore(Noon);

        filter.Matches(new TestFilterableEvent { CreatedAt = Noon.AddTicks(ticksFromBound) }).ShouldBe(expected);
    }

    [Test]
    public void CreatedAtOrAfter_BoundWithAnotherOffset_ComparesInstants()
    {
        var filter = EventFilter.CreatedAtOrAfter(Noon.ToOffset(TimeSpan.FromHours(5)));

        filter.Matches(new TestFilterableEvent { CreatedAt = Noon }).ShouldBeTrue();
        filter.Matches(new TestFilterableEvent { CreatedAt = Noon.AddTicks(-1) }).ShouldBeFalse();
    }

    [Test]
    public void CreatedAtOrAfter_Bound_ReturnsIntervalOpenAtTheEnd()
    {
        var filter = EventFilter.CreatedAtOrAfter(Noon).ShouldBeOfType<CreatedAtFilter>();

        filter.From.ShouldBe(Noon);
        filter.Before.ShouldBeNull();
    }

    [Test]
    public void CreatedBefore_Bound_ReturnsIntervalOpenAtTheStart()
    {
        var filter = EventFilter.CreatedBefore(Noon).ShouldBeOfType<CreatedAtFilter>();

        filter.From.ShouldBeNull();
        filter.Before.ShouldBe(Noon);
    }
}