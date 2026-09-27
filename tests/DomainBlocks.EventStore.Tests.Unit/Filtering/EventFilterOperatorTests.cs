using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Filtering.Nodes;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Unit.Filtering;

public class EventFilterOperatorTests
{
    private static readonly EventFilter A = EventFilter.EventName("A");
    private static readonly EventFilter B = EventFilter.StreamId("order-1");
    private static readonly EventFilter C = EventFilter.Metadata("tenant", "acme");

    [Test]
    public void And_WhenMatched_MatchesWhatBothMatch()
    {
        var filter = A & B;

        filter.Matches(new StoredEvent { EventName = "A", StreamId = "order-1" }).ShouldBeTrue();
        filter.Matches(new StoredEvent { EventName = "A", StreamId = "order-2" }).ShouldBeFalse();
        filter.Matches(new StoredEvent { EventName = "B", StreamId = "order-1" }).ShouldBeFalse();
    }

    [Test]
    public void Or_WhenMatched_MatchesWhatEitherMatches()
    {
        var filter = A | B;

        filter.Matches(new StoredEvent { EventName = "A", StreamId = "order-2" }).ShouldBeTrue();
        filter.Matches(new StoredEvent { EventName = "B", StreamId = "order-1" }).ShouldBeTrue();
        filter.Matches(new StoredEvent { EventName = "B", StreamId = "order-2" }).ShouldBeFalse();
    }

    [Test]
    public void Not_WhenMatched_MatchesWhatTheOperandDoesNot()
    {
        var filter = !C;

        filter.Matches(new StoredEvent { Metadata = { ["tenant"] = "acme" } }).ShouldBeFalse();
        filter.Matches(new StoredEvent { Metadata = { ["tenant"] = "initech" } }).ShouldBeTrue();

        // An event without the entry does not match the filter, so it matches the negation.
        filter.Matches(new StoredEvent()).ShouldBeTrue();
    }

    [Test]
    public void And_WhenAnOperandIsAllOrNone_AppliesIdentityAndAnnihilator()
    {
        (A & EventFilter.All).ShouldBeSameAs(A);
        (EventFilter.All & A).ShouldBeSameAs(A);
        (A & EventFilter.None).ShouldBe(EventFilter.None);
        (EventFilter.None & A).ShouldBe(EventFilter.None);
    }

    [Test]
    public void Or_WhenAnOperandIsAllOrNone_AppliesIdentityAndAnnihilator()
    {
        (A | EventFilter.None).ShouldBeSameAs(A);
        (EventFilter.None | A).ShouldBeSameAs(A);
        (A | EventFilter.All).ShouldBe(EventFilter.All);
        (EventFilter.All | A).ShouldBe(EventFilter.All);
    }

    [Test]
    public void Not_WhenNegatedTwice_GivesTheFilterBack()
    {
        (!!A).ShouldBeSameAs(A);
        (!EventFilter.All).ShouldBe(EventFilter.None);
        (!EventFilter.None).ShouldBe(EventFilter.All);
    }

    [Test]
    public void And_WhenNested_IsFlattened()
    {
        var left = A & B;
        var right = C & !A;
        var filter = left & right;

        filter.ShouldBeOfType<AndFilter>().Operands.ShouldBe([A, B, !A, C]);
        filter.ShouldBe(A & (B & (!A & C)));
    }

    [Test]
    public void Or_WhenNested_IsFlattened()
    {
        var left = A | B;
        var right = C | !A;
        var filter = left | right;

        filter.ShouldBeOfType<OrFilter>().Operands.ShouldBe([A, B, !A, C]);
    }

    [Test]
    public void And_WhenOperandsAreOfOtherKinds_KeepsThemNested()
    {
        var filter = A & (B | C);

        filter.ShouldBeOfType<AndFilter>().Operands.ShouldBe([A, B | C]);
    }

    [Test]
    public void AllOf_WhenGivenFilters_ConjoinsThem()
    {
        EventFilter.AllOf(A, B, C).ShouldBe(A & B & C);
        EventFilter.AllOf(A).ShouldBeSameAs(A);
        EventFilter.AllOf().ShouldBe(EventFilter.All);
    }

    [Test]
    public void AnyOf_WhenGivenFilters_DisjoinsThem()
    {
        EventFilter.AnyOf(A, B, C).ShouldBe(A | B | C);
        EventFilter.AnyOf(A).ShouldBeSameAs(A);
        EventFilter.AnyOf().ShouldBe(EventFilter.None);
    }

    [Test]
    public void And_WhenACheapOperandRulesAnEventOut_DoesNotReadMetadata()
    {
        // Metadata is written first, and still looked at last.
        var filter = C & B;
        var subject = new CountingEvent { StreamId = "order-2" };

        filter.Matches(subject).ShouldBeFalse();

        subject.MetadataReads.ShouldBe(0);
    }

    [Test]
    public void Or_WhenACheapOperandSelectsAnEvent_DoesNotReadMetadata()
    {
        var filter = !C | (C & A) | B;
        var subject = new CountingEvent { StreamId = "order-1" };

        filter.Matches(subject).ShouldBeTrue();

        subject.MetadataReads.ShouldBe(0);
    }

    [Test]
    public void Operators_WhenAnOperandIsNull_Throw()
    {
        Should.Throw<ArgumentNullException>(() => A & null!);
        Should.Throw<ArgumentNullException>(() => null! | A);
        Should.Throw<ArgumentNullException>(() => !(EventFilter)null!);
    }

    private sealed class CountingEvent : IFilterableEvent
    {
        public int MetadataReads { get; private set; }

        public string EventName => "A";

        public required string StreamId { get; init; }

        public DateTimeOffset CreatedAt => default;

        public object DecodedPayload => throw new InvalidOperationException("The event was decoded.");

        public bool TryGetMetadata(string key, out string value)
        {
            MetadataReads++;
            value = "acme";
            return true;
        }
    }
}