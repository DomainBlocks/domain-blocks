using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Filtering.Nodes;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Unit.Filtering;

public class EventFilterOperatorTests
{
    private static readonly EventFilter A = EventFilter.EventNames("A");
    private static readonly EventFilter B = EventFilter.StreamIds("order-1");
    private static readonly EventFilter C = EventFilter.Metadata("tenant", "acme");

    [Test]
    public void And_WhenMatched_RequiresBothOperandsToMatch()
    {
        var filter = A & B;

        filter.Matches(new StoredEvent { EventName = "A", StreamId = "order-1" }).ShouldBeTrue();
        filter.Matches(new StoredEvent { EventName = "A", StreamId = "order-2" }).ShouldBeFalse();
        filter.Matches(new StoredEvent { EventName = "B", StreamId = "order-1" }).ShouldBeFalse();
    }

    [Test]
    public void Or_WhenMatched_RequiresEitherOperandToMatch()
    {
        var filter = A | B;

        filter.Matches(new StoredEvent { EventName = "A", StreamId = "order-2" }).ShouldBeTrue();
        filter.Matches(new StoredEvent { EventName = "B", StreamId = "order-1" }).ShouldBeTrue();
        filter.Matches(new StoredEvent { EventName = "B", StreamId = "order-2" }).ShouldBeFalse();
    }

    [Test]
    public void Not_WhenMatched_InvertsTheOperandResult()
    {
        var filter = !C;

        filter.Matches(new StoredEvent { Metadata = { ["tenant"] = "acme" } }).ShouldBeFalse();
        filter.Matches(new StoredEvent { Metadata = { ["tenant"] = "initech" } }).ShouldBeTrue();
        filter.Matches(new StoredEvent()).ShouldBeTrue();
    }

    [Test]
    public void And_WhenCombinedWithAllOrNone_AppliesIdentityAndAnnihilatorLaws()
    {
        (A & EventFilter.All).ShouldBeSameAs(A);
        (EventFilter.All & A).ShouldBeSameAs(A);
        (A & EventFilter.None).ShouldBe(EventFilter.None);
        (EventFilter.None & A).ShouldBe(EventFilter.None);
    }

    [Test]
    public void Or_WhenCombinedWithAllOrNone_AppliesIdentityAndAnnihilatorLaws()
    {
        (A | EventFilter.None).ShouldBeSameAs(A);
        (EventFilter.None | A).ShouldBeSameAs(A);
        (A | EventFilter.All).ShouldBe(EventFilter.All);
        (EventFilter.All | A).ShouldBe(EventFilter.All);
    }

    [Test]
    public void Not_WhenAppliedTwice_ReturnsTheOriginalFilter()
    {
        (!!A).ShouldBeSameAs(A);
        (!EventFilter.All).ShouldBe(EventFilter.None);
        (!EventFilter.None).ShouldBe(EventFilter.All);
    }

    [Test]
    public void And_WhenNested_FlattensNestedAndFilters()
    {
        var left = A & B;
        var right = C & !A;
        var filter = left & right;

        filter.ShouldBeOfType<AndFilter>().Operands.ShouldBe([A, B, !A, C]);
        filter.ShouldBe(A & (B & (!A & C)));
    }

    [Test]
    public void Or_WhenNested_FlattensNestedOrFilters()
    {
        var left = A | B;
        var right = C | !A;
        var filter = left | right;

        filter.ShouldBeOfType<OrFilter>().Operands.ShouldBe([A, B, !A, C]);
    }

    [Test]
    public void And_WhenOperandIsAnotherOperator_PreservesItsNesting()
    {
        var filter = A & (B | C);

        filter.ShouldBeOfType<AndFilter>().Operands.ShouldBe([A, B | C]);
    }

    [Test]
    public void AllOf_WhenGivenFilters_CombinesThemWithAnd()
    {
        EventFilter.AllOf(A, B, C).ShouldBe(A & B & C);
        EventFilter.AllOf(A).ShouldBeSameAs(A);
        EventFilter.AllOf().ShouldBe(EventFilter.All);
    }

    [Test]
    public void AnyOf_WhenGivenFilters_CombinesThemWithOr()
    {
        EventFilter.AnyOf(A, B, C).ShouldBe(A | B | C);
        EventFilter.AnyOf(A).ShouldBeSameAs(A);
        EventFilter.AnyOf().ShouldBe(EventFilter.None);
    }

    [Test]
    public void And_WhenAnOperandRejectsTheEvent_ShortCircuitsWithoutReadingMetadata()
    {
        var filter = C & B;
        var subject = new CountingEvent { StreamId = "order-2" };

        filter.Matches(subject).ShouldBeFalse();
        subject.MetadataReads.ShouldBe(0);
    }

    [Test]
    public void Or_WhenAnOperandAcceptsTheEvent_ShortCircuitsWithoutReadingMetadata()
    {
        var filter = !C | (C & A) | B;
        var subject = new CountingEvent { StreamId = "order-1" };

        filter.Matches(subject).ShouldBeTrue();
        subject.MetadataReads.ShouldBe(0);
    }

    [Test]
    public void Operators_WhenGivenNullOperands_ThrowArgumentNullException()
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