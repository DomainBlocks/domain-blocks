using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Filtering.Nodes;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Unit.Filtering;

public class EventFilterTests
{
    private static readonly DateTimeOffset Noon = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void All_WhenMatched_MatchesEveryEvent()
    {
        EventFilter.All.Matches(new StoredEvent()).ShouldBeTrue();
    }

    [Test]
    public void None_WhenMatched_MatchesNoEvent()
    {
        EventFilter.None.Matches(new StoredEvent()).ShouldBeFalse();
    }

    [TestCase("OrderPlaced", true)]
    [TestCase("OrderShipped", true)]
    [TestCase("orderplaced", false)]
    [TestCase("InvoiceRaised", false)]
    public void EventNames_WhenMatched_MatchesAnyOfTheNamesExactly(string eventName, bool expected)
    {
        var filter = EventFilter.EventNames("OrderShipped", "OrderPlaced");

        filter.Matches(new StoredEvent { EventName = eventName }).ShouldBe(expected);
    }

    [TestCase("order-1", true)]
    [TestCase("order-2", true)]
    [TestCase("order-10", false)]
    [TestCase("Order-1", false)]
    public void StreamIds_WhenMatched_MatchesAnyOfTheStreamsExactly(string streamId, bool expected)
    {
        var filter = EventFilter.StreamIds("order-2", "order-1");

        filter.Matches(new StoredEvent { StreamId = streamId }).ShouldBe(expected);
    }

    [TestCase("order-1", true)]
    [TestCase("order-", true)]
    [TestCase("Order-1", false)]
    [TestCase("preorder-1", false)]
    public void StreamIdStartsWith_WhenMatched_ComparesOrdinally(string streamId, bool expected)
    {
        var filter = EventFilter.StreamIdStartsWith("order-");

        filter.Matches(new StoredEvent { StreamId = streamId }).ShouldBe(expected);
    }

    [Test]
    public void MetadataExists_WhenMatched_MatchesWhateverTheValue()
    {
        var filter = EventFilter.MetadataExists("tenant");

        filter.Matches(new StoredEvent { Metadata = { ["tenant"] = "" } }).ShouldBeTrue();
        filter.Matches(new StoredEvent { Metadata = { ["Tenant"] = "acme" } }).ShouldBeFalse();
        filter.Matches(new StoredEvent()).ShouldBeFalse();
    }

    [Test]
    public void Metadata_WhenMatched_MatchesAnyOfTheValuesExactly()
    {
        var filter = EventFilter.Metadata("tenant", "acme", "initech");

        filter.Matches(new StoredEvent { Metadata = { ["tenant"] = "initech" } }).ShouldBeTrue();
        filter.Matches(new StoredEvent { Metadata = { ["tenant"] = "Acme" } }).ShouldBeFalse();
        filter.Matches(new StoredEvent { Metadata = { ["user"] = "acme" } }).ShouldBeFalse();
        filter.Matches(new StoredEvent()).ShouldBeFalse();
    }

    [Test]
    public void CreatedAtOrAfter_WhenMatched_IncludesTheBound()
    {
        var filter = EventFilter.CreatedAtOrAfter(Noon);

        filter.Matches(new StoredEvent { CreatedAt = Noon }).ShouldBeTrue();
        filter.Matches(new StoredEvent { CreatedAt = Noon.AddTicks(-1) }).ShouldBeFalse();
    }

    [Test]
    public void CreatedBefore_WhenMatched_ExcludesTheBound()
    {
        var filter = EventFilter.CreatedBefore(Noon);

        filter.Matches(new StoredEvent { CreatedAt = Noon.AddTicks(-1) }).ShouldBeTrue();
        filter.Matches(new StoredEvent { CreatedAt = Noon }).ShouldBeFalse();
    }

    [Test]
    public void CreatedAtOrAfter_WhenTheOffsetDiffers_ComparesTheInstant()
    {
        var filter = EventFilter.CreatedAtOrAfter(Noon.ToOffset(TimeSpan.FromHours(5)));

        filter.Matches(new StoredEvent { CreatedAt = Noon }).ShouldBeTrue();
    }

    [Test]
    public void Factories_WhenGivenNoValues_MatchNoEvent()
    {
        EventFilter.EventNames().ShouldBe(EventFilter.None);
        EventFilter.StreamIds().ShouldBe(EventFilter.None);
        EventFilter.Metadata("tenant").ShouldBe(EventFilter.None);
    }

    [Test]
    public void StreamIdStartsWith_WhenThePrefixIsEmpty_MatchesEveryEvent()
    {
        EventFilter.StreamIdStartsWith("").ShouldBe(EventFilter.All);
    }

    [Test]
    public void Equals_WhenValuesDifferOnlyInOrderOrRepeats_IsTrue()
    {
        EventFilter.EventNames("B", "A", "B").ShouldBe(EventFilter.EventNames("A", "B"));
        EventFilter.StreamIds("s2", "s1").ShouldBe(EventFilter.StreamIds("s1", "s2"));
        EventFilter.Metadata("k", "y", "x").ShouldBe(EventFilter.Metadata("k", "x", "y"));

        EventFilter.EventNames("A", "B").GetHashCode().ShouldBe(EventFilter.EventNames("B", "A").GetHashCode());
    }

    [Test]
    public void Equals_WhenFiltersDiffer_IsFalse()
    {
        EventFilter.EventNames("A").ShouldNotBe(EventFilter.EventNames("A", "B"));
        EventFilter.EventName("A").ShouldNotBe(EventFilter.StreamId("A"));
        EventFilter.Metadata("k", "x").ShouldNotBe(EventFilter.Metadata("j", "x"));
        EventFilter.CreatedBefore(Noon).ShouldNotBe(EventFilter.CreatedAtOrAfter(Noon));
    }

    [Test]
    public void Names_WhenRead_AreSortedWithoutRepeats()
    {
        var filter = (EventNameFilter)EventFilter.EventNames("B", "A", "B");

        filter.Names.ShouldBe(["A", "B"]);
    }

    [Test]
    public void Factories_WhenAValueIsInvalid_Throw()
    {
        Should.Throw<ArgumentException>(() => EventFilter.EventNames("A", " "));
        Should.Throw<ArgumentException>(() => EventFilter.StreamIds("s1", ""));
        Should.Throw<ArgumentException>(() => EventFilter.MetadataExists(""));
        Should.Throw<ArgumentException>(() => EventFilter.Metadata("", "x"));
        Should.Throw<ArgumentNullException>(() => EventFilter.Metadata("k", "x", null!));
        Should.Throw<ArgumentNullException>(() => EventFilter.StreamIdStartsWith(null!));
    }
}