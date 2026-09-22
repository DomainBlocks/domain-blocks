using System.Linq.Expressions;
using DomainBlocks.EventStore;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.TypeMapping;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.Testing.Integration.EventStore.Contract;

public sealed record Parcel
{
    public int Weight { get; init; }

    public ParcelOwner? Owner { get; init; }
}

public sealed record ParcelOwner
{
    public string? Name { get; init; }
}

/// <summary>
/// A parcel whose owner was stored as text, which cannot be read as a <see cref="Parcel"/>.
/// </summary>
public sealed record MisstoredParcel
{
    public int Weight { get; init; }

    public string? Owner { get; init; }
}

/// <summary>
/// For a store whose database can see into payloads. It rules out the events whose stored values contradict a
/// predicate, so they are never read. An event that cannot be read shows whether it was.
/// </summary>
public abstract class EventStoreLoweredPredicateTests<TStreamPos, TLogPos>(
    IEventStoreTestHarness<TStreamPos, TLogPos> harness) :
    EventStoreTestBase<TStreamPos, TLogPos>(harness)
    where TStreamPos : notnull
    where TLogPos : notnull
{
    private const string EventName = "Parcel";

    // Held once, as predicates are equal only if they are the same one.
    private static readonly Expression<Func<Parcel, bool>> IsHeavy = e => e.Weight > 100;
    private static readonly Expression<Func<Parcel, bool>> HasALongName = e => e.Owner!.Name!.Length > 3;

    /// <summary>
    /// The format that events are stored in, or <see langword="null"/> for the default of the harness.
    /// </summary>
    protected virtual EventFormat? Format => null;

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadAll_WhenAStoredValueContradictsAPredicate_DoesNotReadTheEvent(
        CancellationToken cancellationToken)
    {
        RequireCapability(StoreCapabilities.EventFilters);

        var misstoredTypeMap = EventTypeMap.Create(EventTypeMapping.ReadWrite<MisstoredParcel>(EventName));

        await using (var misstoringStore = CreateEventStore(misstoredTypeMap, Format, null, "_misstoring"))
        {
            var light = new MisstoredParcel { Weight = 5, Owner = "Ann" };
            await misstoringStore.AppendAsync(
                "parcel-1",
                [AppendableEvent.Create<object>(light)],
                cancellationToken: cancellationToken);
        }

        await using var eventStore = CreateEventStore(
            EventTypeMap.Create(EventTypeMapping.ReadWrite<Parcel>(EventName)),
            Format);

        var heavy = new Parcel { Weight = 500, Owner = new ParcelOwner { Name = "Bob" } };
        await eventStore.AppendAsync(
            "parcel-1",
            [AppendableEvent.Create<object>(heavy)],
            cancellationToken: cancellationToken);

        var filter = EventFilter.OfType(IsHeavy);

        // In process, every parcel is read to be tested, and the light one cannot be.
        var inProcess = new ReadAllOptions { Filter = filter, FilterPushdownMode = FilterPushdownMode.None };

        await Should.ThrowAsync<Exception>(
            async () => await eventStore.ReadAll(options: inProcess).ToArrayAsync(cancellationToken));

        // The database rules it out by its stored weight.
        var read = await eventStore.ReadAll(options: new() { Filter = filter }).ToArrayAsync(cancellationToken);

        read.ShouldHaveSingleItem().Payload.ShouldBe(heavy);
    }

    [Test]
    public async Task ExplainFilter_WhenGivenAPredicate_SaysWhetherTheDatabaseHelpsWithIt()
    {
        RequireCapability(StoreCapabilities.EventFilters);

        await using var eventStore = CreateEventStore(
            EventTypeMap.Create(EventTypeMapping.ReadWrite<Parcel>(EventName)),
            Format);

        var helped = eventStore.ExplainFilter(EventFilter.OfType(IsHeavy));

        // The predicate is always left to test, as only the event that is read says whether it is true.
        helped.IsNarrowedByPayload.ShouldBeTrue();
        helped.Residual.ShouldBe(EventFilter.OfType(IsHeavy));

        // The length of a name is not stored, so every parcel is read to be tested.
        var unhelped = eventStore.ExplainFilter(EventFilter.OfType(HasALongName));

        unhelped.IsNarrowedByPayload.ShouldBeFalse();
        unhelped.Pushdown.ShouldBe(EventFilter.EventName(EventName));
        unhelped.Residual.ShouldBe(EventFilter.OfType(HasALongName));
    }

    [Test]
    public async Task ExplainFilter_WhenGivenHowMuchToPushDown_PlansAsAReadWould()
    {
        RequireCapability(StoreCapabilities.EventFilters);

        await using var eventStore = CreateEventStore(
            EventTypeMap.Create(EventTypeMapping.ReadWrite<Parcel>(EventName)),
            Format);

        var filter = EventFilter.StreamId("parcel-1") & EventFilter.OfType(IsHeavy);

        eventStore.ExplainFilter(filter, FilterPushdownMode.None).Pushdown.ShouldBe(EventFilter.All);
        eventStore.ExplainFilter(EventFilter.StreamId("parcel-1"), FilterPushdownMode.Require).Residual
            .ShouldBe(EventFilter.All);

        Should.Throw<EventFilterNotSupportedException>(
            () => eventStore.ExplainFilter(filter, FilterPushdownMode.Require));
    }
}