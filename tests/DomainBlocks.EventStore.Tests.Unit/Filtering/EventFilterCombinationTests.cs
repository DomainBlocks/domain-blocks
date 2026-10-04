using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Filtering.Nodes;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Unit.Filtering;

public class EventFilterCombinationTests
{
    private static readonly DateTimeOffset Noon = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly EventFilter A = EventFilter.EventNames("OrderPlaced");
    private static readonly EventFilter B = EventFilter.StreamIds("order-1");
    private static readonly EventFilter C = EventFilter.MetadataExists("tenant");
    private static readonly EventFilter D = EventFilter.CreatedBefore(Noon);

    // Every combination of two names, two streams, three states of metadata, and two creation times.
    private static readonly TestFilterableEvent[] Events =
    [
        .. from eventName in (string[])["OrderPlaced", "OrderShipped"]
        from streamId in (string[])["order-1", "invoice-1"]
        from tenant in (string?[])[null, "acme", "initech"]
        from createdAt in (DateTimeOffset[])[Noon.AddHours(-1), Noon.AddHours(1)]
        select new TestFilterableEvent
        {
            EventName = eventName,
            StreamId = streamId,
            CreatedAt = createdAt,
            Metadata = tenant is null ? [] : new Dictionary<string, string> { ["tenant"] = tenant }
        }
    ];

    // Every kind of leaf, both constants, and some filters that are already combined.
    private static readonly EventFilter[] Filters =
    [
        A,
        B,
        C,
        D,
        EventFilter.StreamIdStartsWith("order-"),
        EventFilter.Metadata("tenant", "acme"),
        EventFilter.CreatedAtOrAfter(Noon),
        EventFilter.All,
        EventFilter.None,
        A & C,
        EventFilter.StreamIds("invoice-1") | EventFilter.Metadata("tenant", "initech"),
        !EventFilter.EventNames("OrderShipped")
    ];

    [Test]
    public void And_AnyTwoFilters_MatchesEventsThatBothMatch()
    {
        ForEachPair((a, b, e) => (a & b).Matches(e).ShouldBe(a.Matches(e) && b.Matches(e), $"{a} & {b}"));
    }

    [Test]
    public void Or_AnyTwoFilters_MatchesEventsThatEitherMatches()
    {
        ForEachPair((a, b, e) => (a | b).Matches(e).ShouldBe(a.Matches(e) || b.Matches(e), $"{a} | {b}"));
    }

    [Test]
    public void Not_AnyFilter_MatchesEventsThatItDoesNotMatch()
    {
        foreach (var a in Filters)
        {
            foreach (var e in Events)
                (!a).Matches(e).ShouldBe(!a.Matches(e), $"!{a}");
        }
    }

    [Test]
    public void Not_OfAndOrOr_MatchesAsDeMorgansLawsSay()
    {
        ForEachPair((a, b, e) =>
        {
            (!(a & b)).Matches(e).ShouldBe((!a | !b).Matches(e), $"!({a} & {b})");
            (!(a | b)).Matches(e).ShouldBe((!a & !b).Matches(e), $"!({a} | {b})");
        });
    }

    [Test]
    public void AndAndOr_AnyThreeFilters_AreAssociativeAndDistributive()
    {
        foreach (var a in Filters)
        {
            foreach (var b in Filters)
            {
                foreach (var c in Filters)
                {
                    foreach (var e in Events)
                    {
                        var because = $"{a}, {b}, {c}";

                        (a & b & c).Matches(e).ShouldBe((a & (b & c)).Matches(e), because);
                        (a | b | c).Matches(e).ShouldBe((a | (b | c)).Matches(e), because);
                        (a & (b | c)).Matches(e).ShouldBe(((a & b) | (a & c)).Matches(e), because);
                        (a | (b & c)).Matches(e).ShouldBe(((a | b) & (a | c)).Matches(e), because);
                    }
                }
            }
        }
    }

    [Test]
    public void And_WithAll_ReturnsTheOtherFilter()
    {
        (EventFilter.All & A).ShouldBeSameAs(A);
        (A & EventFilter.All).ShouldBeSameAs(A);
    }

    [Test]
    public void And_WithNone_ReturnsNone()
    {
        (EventFilter.None & A).ShouldBeSameAs(EventFilter.None);
        (A & EventFilter.None).ShouldBeSameAs(EventFilter.None);
    }

    [Test]
    public void Or_WithNone_ReturnsTheOtherFilter()
    {
        (EventFilter.None | A).ShouldBeSameAs(A);
        (A | EventFilter.None).ShouldBeSameAs(A);
    }

    [Test]
    public void Or_WithAll_ReturnsAll()
    {
        (EventFilter.All | A).ShouldBeSameAs(EventFilter.All);
        (A | EventFilter.All).ShouldBeSameAs(EventFilter.All);
    }

    [Test]
    public void Not_OfAllOrNone_ReturnsTheOther()
    {
        (!EventFilter.All).ShouldBeSameAs(EventFilter.None);
        (!EventFilter.None).ShouldBeSameAs(EventFilter.All);
    }

    [Test]
    public void Not_OfNot_ReturnsTheOriginalFilter()
    {
        var negation = !A;

        negation.ShouldBeOfType<NotFilter>().Operand.ShouldBeSameAs(A);
        (!negation).ShouldBeSameAs(A);
    }

    [Test]
    public void And_OfAnds_KeepsOperandsInOrderInOneNode()
    {
        (A & B & (C & D)).ShouldBeOfType<AndFilter>().Operands.ShouldBe([A, B, C, D]);
        (A & (B & C)).ShouldBeOfType<AndFilter>().Operands.ShouldBe([A, B, C]);
    }

    [Test]
    public void Or_OfOrs_KeepsOperandsInOrderInOneNode()
    {
        (A | B | (C | D)).ShouldBeOfType<OrFilter>().Operands.ShouldBe([A, B, C, D]);
        (A | (B | C)).ShouldBeOfType<OrFilter>().Operands.ShouldBe([A, B, C]);
    }

    [Test]
    public void And_OfOr_KeepsTheOrAsOneOperand()
    {
        var or = B | C;

        (A & or).ShouldBeOfType<AndFilter>().Operands.ShouldBe([A, or]);
    }

    [Test]
    public void AllOf_SeveralFilters_MatchesAsTheirConjunction()
    {
        EventFilter.AllOf(A, B, C).ShouldBeOfType<AndFilter>().Operands.ShouldBe([A, B, C]);
        EventFilter.AllOf(A).ShouldBeSameAs(A);
        EventFilter.AllOf(A, EventFilter.None, B).ShouldBeSameAs(EventFilter.None);
    }

    [Test]
    public void AllOf_NoFilters_ReturnsAll()
    {
        EventFilter.AllOf().ShouldBeSameAs(EventFilter.All);
    }

    [Test]
    public void AnyOf_SeveralFilters_MatchesAsTheirDisjunction()
    {
        EventFilter.AnyOf(A, B, C).ShouldBeOfType<OrFilter>().Operands.ShouldBe([A, B, C]);
        EventFilter.AnyOf(A).ShouldBeSameAs(A);
        EventFilter.AnyOf(A, EventFilter.All, B).ShouldBeSameAs(EventFilter.All);
    }

    [Test]
    public void AnyOf_NoFilters_ReturnsNone()
    {
        EventFilter.AnyOf().ShouldBeSameAs(EventFilter.None);
    }

    [Test]
    public void Operators_NullOperand_Throws()
    {
        Should.Throw<ArgumentNullException>(() => A & null!);
        Should.Throw<ArgumentNullException>(() => null! & A);
        Should.Throw<ArgumentNullException>(() => A | null!);
        Should.Throw<ArgumentNullException>(() => null! | A);
        Should.Throw<ArgumentNullException>(() => !(EventFilter)null!);
    }

    [Test]
    public void AllOfAndAnyOf_NullFilters_Throws()
    {
        Should.Throw<ArgumentNullException>(() => EventFilter.AllOf(null!));
        Should.Throw<ArgumentNullException>(() => EventFilter.AnyOf(null!));
        Should.Throw<ArgumentNullException>(() => EventFilter.AllOf(A, null!));
        Should.Throw<ArgumentNullException>(() => EventFilter.AnyOf(A, null!));
    }

    [Test]
    public void ToString_Leaves_ReadsAsTheFactoryCalls()
    {
        EventFilter.All.ToString().ShouldBe("All");
        EventFilter.None.ToString().ShouldBe("None");
        EventFilter.EventNames("OrderShipped", "OrderPlaced").ToString().ShouldBe(
            "EventNames(\"OrderPlaced\", \"OrderShipped\")");
        EventFilter.StreamIds("order-1").ToString().ShouldBe("StreamIds(\"order-1\")");
        EventFilter.StreamIdStartsWith("order-").ToString().ShouldBe("StreamIdStartsWith(\"order-\")");
        EventFilter.MetadataExists("tenant").ToString().ShouldBe("MetadataExists(\"tenant\")");
        EventFilter.Metadata("tenant", "acme", "").ToString().ShouldBe("Metadata(\"tenant\", \"\", \"acme\")");
    }

    [Test]
    public void ToString_CreatedAtBounds_WritesInstantsInUtc()
    {
        var instant = new DateTimeOffset(2026, 1, 1, 14, 30, 0, TimeSpan.FromHours(2));

        EventFilter.CreatedAtOrAfter(instant).ToString().ShouldBe("CreatedAtOrAfter(2026-01-01T12:30:00.0000000Z)");
        EventFilter.CreatedBefore(instant).ToString().ShouldBe("CreatedBefore(2026-01-01T12:30:00.0000000Z)");
    }

    [Test]
    public void ToString_CombinedFilters_ReadsAsTheExpression()
    {
        var filter = A & (B | !C) & !(A | B);

        filter.ToString().ShouldBe(
            "(EventNames(\"OrderPlaced\") & (StreamIds(\"order-1\") | !MetadataExists(\"tenant\")) & " +
            "!(EventNames(\"OrderPlaced\") | StreamIds(\"order-1\")))");
    }

    [Test]
    public void ToString_ValueWithQuoteOrBackslash_EscapesIt()
    {
        EventFilter.StreamIds("a\"b\\c").ToString().ShouldBe("StreamIds(\"a\\\"b\\\\c\")");
    }

    private static void ForEachPair(Action<EventFilter, EventFilter, TestFilterableEvent> assert)
    {
        foreach (var a in Filters)
        {
            foreach (var b in Filters)
            {
                foreach (var e in Events)
                    assert(a, b, e);
            }
        }
    }
}