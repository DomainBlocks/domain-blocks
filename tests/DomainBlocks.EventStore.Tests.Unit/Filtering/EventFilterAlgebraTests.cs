using DomainBlocks.EventStore.Filtering;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Unit.Filtering;

/// <summary>
/// Filters form a Boolean algebra under <c>&amp;</c>, <c>|</c> and <c>!</c>, with <c>All</c> and <c>None</c> as its
/// top and bottom. The laws are checked by what filters match, not by how they are built: <c>a &amp; b</c> and
/// <c>b &amp; a</c> are different filters that match the same events.
/// </summary>
public class EventFilterAlgebraTests
{
    private const int Samples = 300;

    [Test]
    public void Universe_WhenListed_HasEventsThatRandomFiltersTellApart()
    {
        var random = new Random(1);
        var filters = Enumerable.Range(0, Samples).Select(_ => EventFilterUniverse.NextFilter(random)).ToArray();

        // The laws below would hold trivially if filters matched everything, or nothing.
        filters.Count(x => EventFilterUniverse.Events.Any(x.Matches)).ShouldBeGreaterThan(Samples / 2);
        filters.Count(x => !EventFilterUniverse.Events.All(x.Matches)).ShouldBeGreaterThan(Samples / 2);
    }

    [Test]
    public void Matches_WhenGivenAnyFilter_AgreesWithWhatTheFilterMeans()
    {
        // The laws below compare what filters match with each other, which says nothing of whether either is right.
        var random = new Random(6);

        for (var i = 0; i < Samples; i++)
        {
            var filter = EventFilterUniverse.NextFilter(random);

            foreach (var e in EventFilterUniverse.Events)
                filter.Matches(e).ShouldBe(EventFilterOracle.Matches(filter, e), $"{filter} of {e}");
        }
    }

    [Test]
    public void Operators_WhenGivenAnyFilters_ObeyTheLawsOfBooleanAlgebra()
    {
        var random = new Random(2);
        var all = EventFilter.All;
        var none = EventFilter.None;

        for (var i = 0; i < Samples; i++)
        {
            var a = EventFilterUniverse.NextFilter(random);
            var b = EventFilterUniverse.NextFilter(random);
            var c = EventFilterUniverse.NextFilter(random);

            ShouldBeEquivalent("identity of &", a & all, a);
            ShouldBeEquivalent("identity of |", a | none, a);
            ShouldBeEquivalent("annihilator of &", a & none, none);
            ShouldBeEquivalent("annihilator of |", a | all, all);
            ShouldBeEquivalent("idempotence of &", a & a, a);
            ShouldBeEquivalent("idempotence of |", a | a, a);
            ShouldBeEquivalent("complement of &", a & !a, none);
            ShouldBeEquivalent("complement of |", a | !a, all);
            ShouldBeEquivalent("involution", !!a, a);
            ShouldBeEquivalent("commutativity of &", a & b, b & a);
            ShouldBeEquivalent("commutativity of |", a | b, b | a);
            ShouldBeEquivalent("associativity of &", a & (b & c), a & b & c);
            ShouldBeEquivalent("associativity of |", a | (b | c), a | b | c);
            ShouldBeEquivalent("absorption of &", a & (a | b), a);
            ShouldBeEquivalent("absorption of |", a | (a & b), a);
            ShouldBeEquivalent("distributivity of &", a & (b | c), (a & b) | (a & c));
            ShouldBeEquivalent("distributivity of |", a | (b & c), (a | b) & (a | c));
            ShouldBeEquivalent("De Morgan of &", !(a & b), !a | !b);
            ShouldBeEquivalent("De Morgan of |", !(a | b), !a & !b);
        }
    }

    [Test]
    public void ToString_WhenGivenAnyFilters_IsTheSameOnlyForEqualFilters()
    {
        // What names a filter on a server will rely on this.
        var random = new Random(3);

        for (var i = 0; i < Samples; i++)
        {
            var a = EventFilterUniverse.NextFilter(random);
            var b = EventFilterUniverse.NextFilter(random);

            if (a.ToString() == b.ToString())
                a.ShouldBe(b);
            else
                a.ShouldNotBe(b);
        }
    }

    private static void ShouldBeEquivalent(string law, EventFilter left, EventFilter right)
    {
        EventFilterUniverse.AreEquivalent(left, right).ShouldBeTrue($"{law}: {left} and {right}");
    }
}