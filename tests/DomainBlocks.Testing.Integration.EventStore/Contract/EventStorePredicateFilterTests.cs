using DomainBlocks.EventStore;
using DomainBlocks.EventStore.Filtering;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.Testing.Integration.EventStore.Contract;

/// <summary>
/// A predicate selects by the event as it is read. However much of a filter a store pushes down, it selects the events
/// that the predicate is true of, and what those are is said here by the predicate itself.
/// </summary>
public abstract class EventStorePredicateFilterTests<TStreamPos, TLogPos>(
    IEventStoreTestHarness<TStreamPos, TLogPos> harness) :
    EventStoreTestBase<TStreamPos, TLogPos>(harness)
    where TStreamPos : notnull
    where TLogPos : notnull
{
    private IEventStore<object, string, TStreamPos, TLogPos> EventStore { get; set; } = null!;

    private IReadOnlyList<ReadEvent<object, string, TStreamPos, TLogPos>> Log { get; set; } = null!;

    public static IEnumerable<PredicateFilterCase> Cases => PredicateFilterTestLog.Cases;

    /// <summary>
    /// The format that events are stored in, or <see langword="null"/> for the default of the harness.
    /// </summary>
    protected virtual EventFormat? Format => null;

    [OneTimeSetUp]
    public async Task AppendLogAsync()
    {
        EventStore = CreateEventStore(PredicateFilterTestLog.TypeMap, Format);

        foreach (var e in PredicateFilterTestLog.Events())
            await EventStore.AppendAsync("basket-1", [e]);

        Log = await EventStore.ReadAll().ToArrayAsync();
        Log.Count.ShouldBe(25);

        // A predicate that selects no basket, or every basket, would pass whatever a store did with it.
        var basketCount = Log.Count(x => x.Payload is Basket);

        foreach (var predicateCase in Cases)
        {
            var count = Log.Count(x => IsSelected(predicateCase, x));

            count.ShouldBeGreaterThan(0, predicateCase.Name);
            count.ShouldBeLessThan(basketCount, predicateCase.Name);
        }
    }

    [OneTimeTearDown]
    public async Task DisposeStoreAsync()
    {
        if (EventStore is { } eventStore)
            await eventStore.DisposeAsync();
    }

    [TestCaseSource(nameof(Cases))]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadAll_WithAPredicate_SelectsTheEventsItIsTrueOf(
        PredicateFilterCase predicateCase,
        CancellationToken cancellationToken)
    {
        RequireCapability(StoreCapabilities.EventFilters);

        var byPredicate = EventFilter.OfType(predicateCase.Predicate);
        var invoices = EventFilter.EventNames(nameof(InvoiceRaised));

        (EventFilter Filter, Func<bool, bool, bool> IsExpected)[] filters =
        [
            (byPredicate, (isSelected, _) => isSelected),
            (!byPredicate, (isSelected, _) => !isSelected),
            (byPredicate | invoices, (isSelected, isInvoice) => isSelected || isInvoice),
            (!(byPredicate | invoices), (isSelected, isInvoice) => !(isSelected || isInvoice))
        ];

        foreach (var (filter, isExpected) in filters)
        {
            var expected = Log
                .Where(x => isExpected(IsSelected(predicateCase, x), x.Payload is InvoiceRaised))
                .Select(x => x.Context.LogPosition)
                .ToArray();

            foreach (var pushdown in (FilterPushdownMode[])[FilterPushdownMode.Prefer, FilterPushdownMode.None])
            {
                var options = new ReadAllOptions { Filter = filter, FilterPushdownMode = pushdown };
                var read = await EventStore.ReadAll(options: options).ToArrayAsync(cancellationToken);

                read.Select(x => x.Context.LogPosition).ShouldBe(expected, $"{pushdown}, {filter}");
            }
        }
    }

    private static bool IsSelected(
        PredicateFilterCase predicateCase,
        ReadEvent<object, string, TStreamPos, TLogPos> e)
    {
        try
        {
            return e.Payload is Basket basket && predicateCase.Compiled(basket);
        }
        catch (NullReferenceException)
        {
            return false;
        }
    }
}