using DomainBlocks.EventStore.Filtering;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Unit.Filtering;

public class EventFilterToStringTests
{
    [Test]
    public void ToString_WhenALeaf_NamesItAndItsValues()
    {
        EventFilter.All.ToString().ShouldBe("all");
        EventFilter.None.ToString().ShouldBe("none");
        EventFilter.EventNames("B", "A").ToString().ShouldBe("""eventName("A","B")""");
        EventFilter.StreamId("order-1").ToString().ShouldBe("""streamId("order-1")""");
        EventFilter.StreamIdStartsWith("order-").ToString().ShouldBe("""streamIdPrefix("order-")""");
        EventFilter.MetadataExists("tenant").ToString().ShouldBe("""metadataExists("tenant")""");
        EventFilter.Metadata("tenant", "b", "a").ToString().ShouldBe("""metadata("tenant","a","b")""");
    }

    [Test]
    public void ToString_WhenCreatedAt_WritesTheInstantInUtc()
    {
        var noon = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        EventFilter.CreatedAtOrAfter(noon).ToString().ShouldBe("createdAt(2026-01-01T12:00:00.0000000Z,*)");
        EventFilter.CreatedBefore(noon).ToString().ShouldBe("createdAt(*,2026-01-01T12:00:00.0000000Z)");

        EventFilter.CreatedBefore(noon.ToOffset(TimeSpan.FromHours(5))).ToString()
            .ShouldBe(EventFilter.CreatedBefore(noon).ToString());
    }

    [Test]
    public void ToString_WhenCombined_NestsTheOperands()
    {
        var filter = EventFilter.EventName("A") & (EventFilter.StreamId("s") | !EventFilter.MetadataExists("k"));

        filter.ToString().ShouldBe("""and(eventName("A"),or(streamId("s"),not(metadataExists("k"))))""");
    }

    [Test]
    public void ToString_WhenAValueHasAQuoteOrBackslash_EscapesIt()
    {
        EventFilter.StreamId("""a"b\c""").ToString().ShouldBe("""streamId("a\"b\\c")""");
    }

    [Test]
    public void ToString_WhenValuesCouldRunTogether_KeepsThemApart()
    {
        var oneValue = EventFilter.Metadata("k", """a","b""");
        var twoValues = EventFilter.Metadata("k", "a", "b");

        oneValue.ToString().ShouldNotBe(twoValues.ToString());
    }

    [Test]
    public void ToString_WhenFiltersAreEqual_IsTheSame()
    {
        var left = EventFilter.EventNames("A", "B") & EventFilter.StreamIds("s2", "s1");
        var right = EventFilter.EventNames("B", "A") & EventFilter.StreamIds("s1", "s2");

        left.ToString().ShouldBe(right.ToString());
    }
}