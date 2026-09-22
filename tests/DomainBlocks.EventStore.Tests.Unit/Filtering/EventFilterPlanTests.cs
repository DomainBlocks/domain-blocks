using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Filtering.Nodes;
using DomainBlocks.EventStore.Tests.Shared;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Unit.Filtering;

public class EventFilterPlanTests
{
    private static readonly EventFilter Name = EventFilter.EventName("OrderPlaced");
    private static readonly EventFilter Stream = EventFilter.StreamId("order-1");
    private static readonly EventFilter Tenant = EventFilter.Metadata("tenant", "acme");
    private static readonly EventFilter OrderNames = EventFilter.EventNames("OrderPlaced", "OrderShipped");
    private static readonly EventFilter IsLarge = EventFilter.OfType<EventFilterUniverse.Order>(e => e.Total > 100);

    [Test]
    public void Create_WhenEveryLeafCanBePushed_LeavesNoResidual()
    {
        var filter = Name & (Stream | !Tenant);

        var plan = Plan(filter, canPushdown: _ => true);

        plan.Pushdown.ShouldBe(filter);
        plan.Residual.ShouldBe(EventFilter.All);
    }

    [Test]
    public void Create_WhenConjunctCannotBePushed_FiltersByResidual()
    {
        var plan = Plan(Name & Stream & Tenant, canPushdown: x => x is not MetadataValueFilter);

        plan.Pushdown.ShouldBe(Name & Stream);
        plan.Residual.ShouldBe(Tenant);
    }

    [Test]
    public void Create_WhenDisjunctCannotBePushed_PushesNothingOfTheDisjunction()
    {
        var plan = Plan(Name & (Stream | Tenant), canPushdown: x => x is not MetadataValueFilter);

        plan.Pushdown.ShouldBe(Name);
        plan.Residual.ShouldBe(Stream | Tenant);
    }

    [Test]
    public void Create_WhenNegatedLeafCannotBePushed_FiltersByResidual()
    {
        var plan = Plan(Name & !(Stream & Tenant), canPushdown: x => x is not MetadataValueFilter);

        plan.Pushdown.ShouldBe(Name);
        plan.Residual.ShouldBe(!(Stream & Tenant));
    }

    [Test]
    public void Create_WhenPartOfNestedConjunctionCanBePushed_PushesThatPart()
    {
        var plan = Plan(Name | (Stream & Tenant), canPushdown: x => x is not MetadataValueFilter);

        plan.Pushdown.ShouldBe(Name | Stream);
        plan.Residual.ShouldBe(Name | (Stream & Tenant));
    }

    [Test]
    public void Create_WhenFilterIsAllOrNone_PushesAsIs()
    {
        Plan(EventFilter.All, canPushdown: _ => false)
            .ShouldBe(new EventFilterPlan(EventFilter.All, EventFilter.All));

        Plan(EventFilter.None, canPushdown: _ => false)
            .ShouldBe(new EventFilterPlan(EventFilter.None, EventFilter.All));
    }

    [Test]
    public void Create_WhenPushdownModeIsNone_FiltersByResidualOnly()
    {
        var plan = Plan(Name & Tenant, canPushdown: _ => true, FilterPushdownMode.None);

        plan.Pushdown.ShouldBe(EventFilter.All);
        plan.Residual.ShouldBe(Name & Tenant);
    }

    [Test]
    public void Create_WhenPushdownIsRequiredAndHasResidual_Throws()
    {
        var exception = Should.Throw<EventFilterNotSupportedException>(() =>
            Plan(Name & Tenant, canPushdown: x => x is not MetadataValueFilter, FilterPushdownMode.Require));

        exception.Message.ShouldContain(Tenant.ToString());
    }

    [Test]
    public void Create_WhenPushdownIsRequiredAndHasNoResidual_DoesNotThrow()
    {
        Plan(Name & Tenant, canPushdown: _ => true, FilterPushdownMode.Require).Residual.ShouldBe(EventFilter.All);
    }

    [Test]
    public void Create_WhenFilterIsOfType_PushesEquivalentNamesFilter()
    {
        var plan = Plan(EventFilter.OfType<EventFilterUniverse.IOrderEvent>(), canPushdown: x => x is EventNameFilter);

        plan.Pushdown.ShouldBe(OrderNames);
        plan.Residual.ShouldBe(EventFilter.All);
    }

    [Test]
    public void Create_WhenNoNameIsReadAsTheType_SelectsNothing()
    {
        Plan(EventFilter.OfType<string>(), canPushdown: _ => true).Pushdown.ShouldBe(EventFilter.None);
    }

    [Test]
    public void Create_WhenOfTypeFilterHasPredicate_PushesEquivalentNamesFilterOnly()
    {
        var predicate = EventFilter.OfType<EventFilterUniverse.Order>(e => e.Customer != null);

        var plan = Plan(Stream & predicate, canPushdown: x => x is not EventTypeFilter);

        plan.Pushdown.ShouldBe(Stream & OrderNames);
        plan.Residual.ShouldBe(predicate);
    }

    [Test]
    public void Create_WhenStoredValueMayContradictPredicate_NarrowsByStoredValue()
    {
        var plan = Plan(Stream & IsLarge, canPushdown: x => x is not EventTypeFilter);

        plan.Pushdown.ShouldBe(Stream & OrderNames & !StoredPayload.At("total").LessThanOrEqualTo(100));
        plan.Residual.ShouldBe(IsLarge);
    }

    [Test]
    public void Create_WhenOrOperandIsPredicate_NarrowsByIt()
    {
        var plan = Plan(IsLarge | Tenant, canPushdown: x => x is not EventTypeFilter);

        plan.Pushdown.ShouldBe((OrderNames & !StoredPayload.At("total").LessThanOrEqualTo(100)) | Tenant);
        plan.Residual.ShouldBe((OrderNames & IsLarge) | Tenant);
    }

    [Test]
    public void Create_WhenPredicateFilterIsNegated_DoesNotNarrowByIt()
    {
        Plan(Stream & !IsLarge, canPushdown: x => x is not EventTypeFilter).Pushdown.ShouldBe(Stream);
    }

    [Test]
    public void Create_WhenPayloadFiltersCannotBePushedDown_PushesNamesOnly()
    {
        var plan = Plan(IsLarge, canPushdown: x => x is EventNameFilter);

        plan.Pushdown.ShouldBe(OrderNames);
    }

    [Test]
    public void Create_WithNoStoredPathResolver_PushesNamesOnly()
    {
        var plan = EventFilterPlan.Create(
            IsLarge,
            FilterPushdownMode.Prefer,
            EventFilterUniverse.GetEventNames,
            canPushdown: x => x is not EventTypeFilter);

        plan.Pushdown.ShouldBe(OrderNames);
    }

    [Test]
    public void Create_WhenPushdownIsRequiredAndFilterHasResidual_Throws()
    {
        Should.Throw<EventFilterNotSupportedException>(() =>
            Plan(IsLarge, canPushdown: x => x is not EventTypeFilter, FilterPushdownMode.Require));
    }

    [Test]
    public void IsMetadataRequired_WhenResidualHasMetadataLeaf_IsTrue()
    {
        Plan(Name & !Tenant, canPushdown: x => x is EventNameFilter).IsMetadataRequired.ShouldBeTrue();
        Plan(Name & !Tenant, canPushdown: _ => true).IsMetadataRequired.ShouldBeFalse();
    }

    [Test]
    public void Create_WhenGivenAnyFilterAndAnyPushableFilterTypes_SelectsTheSameEvents()
    {
        var random = new Random(4);

        for (var i = 0; i < 500; i++)
        {
            var filter = EventFilterUniverse.NextFilter(random);

            // A database that can evaluate some kinds of leaf and not others.
            var pushableTypes = LeafTypes.Where(_ => random.Next(2) == 0).ToHashSet();

            var plan = Plan(filter, canPushdown: x => pushableTypes.Contains(x.GetType()));

            plan.Pushdown
                .GetLeafNodes()
                .Where(x => x is not (AllEventsFilter or NoEventsFilter))
                .All(x => pushableTypes.Contains(x.GetType()))
                .ShouldBeTrue($"{filter} as {plan}");

            foreach (var e in EventFilterUniverse.Events)
            {
                var expected = EventFilterOracle.Matches(filter, e);

                // What goes to the database is for it alone to evaluate, so the oracle stands in for it.
                var isPushed = EventFilterOracle.Matches(plan.Pushdown, e);

                // The database must not lose an event, and the two parts together must select what the filter does.
                if (expected)
                    isPushed.ShouldBeTrue($"{filter} as {plan}");

                (isPushed && plan.Residual.Matches(e)).ShouldBe(expected, $"{filter} as {plan}");
            }
        }
    }

    [Test]
    public void ResolveTypes_WhenGivenAnyFilter_SelectsTheSameEvents()
    {
        var random = new Random(5);

        for (var i = 0; i < 500; i++)
        {
            var filter = EventFilterUniverse.NextFilter(random);
            var resolved = filter.LowerEventTypes(EventFilterUniverse.GetEventNames);

            EventFilterUniverse.AreEquivalent(filter, resolved).ShouldBeTrue($"{filter} as {resolved}");
            resolved.GetLeafNodes().Any(x => x is EventTypeFilter { Predicate: null }).ShouldBeFalse();
        }
    }

    private static readonly Type[] LeafTypes =
    [
        typeof(EventNameFilter),
        typeof(StreamIdFilter),
        typeof(StreamIdPrefixFilter),
        typeof(MetadataExistsFilter),
        typeof(MetadataValueFilter),
        typeof(CreatedAtFilter),
        typeof(PayloadValueFilter)
    ];

    private static EventFilterPlan Plan(
        EventFilter filter,
        Func<EventFilter, bool> canPushdown,
        FilterPushdownMode pushdownMode = FilterPushdownMode.Prefer)
    {
        return EventFilterPlan.Create(
            filter,
            pushdownMode,
            EventFilterUniverse.GetEventNames,
            canPushdown,
            EventFilterUniverse.GetStoredPath);
    }
}