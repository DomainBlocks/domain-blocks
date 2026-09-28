using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Filtering.Nodes;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Unit.Filtering;

public class EventFilterPlanTests
{
    private static readonly EventFilter Name = EventFilter.EventNames("OrderPlaced");
    private static readonly EventFilter Stream = EventFilter.StreamIds("order-1");
    private static readonly EventFilter Tenant = EventFilter.Metadata("tenant", "acme");
    private static readonly EventFilter OrderNames = EventFilter.EventNames("OrderPlaced", "OrderShipped");
    private static readonly EventFilter IsLarge = EventFilter.OfType<EventFilterUniverse.Order>(e => e.Total > 100);

    [Test]
    public void Create_WhenAllLeavesArePushable_PushesTheEntireFilter()
    {
        var filter = Name & (Stream | !Tenant);

        var plan = Plan(filter, canPushdown: _ => true);

        plan.Pushdown.ShouldBe(filter);
        plan.Residual.ShouldBe(EventFilter.All);
    }

    [Test]
    public void Create_WhenConjunctionContainsNonPushableOperand_PushesOnlyPushableOperands()
    {
        var plan = Plan(Name & Stream & Tenant, canPushdown: x => x is not MetadataValueFilter);

        plan.Pushdown.ShouldBe(Name & Stream);
        plan.Residual.ShouldBe(Tenant);
    }

    [Test]
    public void Create_WhenDisjunctionContainsNonPushableOperand_LeavesTheDisjunctionAsResidual()
    {
        var plan = Plan(Name & (Stream | Tenant), canPushdown: x => x is not MetadataValueFilter);

        plan.Pushdown.ShouldBe(Name);
        plan.Residual.ShouldBe(Stream | Tenant);
    }

    [Test]
    public void Create_WhenNegatedOperandIsNotPushable_LeavesTheNegationAsResidual()
    {
        var plan = Plan(Name & !(Stream & Tenant), canPushdown: x => x is not MetadataValueFilter);

        plan.Pushdown.ShouldBe(Name);
        plan.Residual.ShouldBe(!(Stream & Tenant));
    }

    [Test]
    public void Create_WhenNestedConjunctionContainsPushableOperand_PushesThatOperand()
    {
        var plan = Plan(Name | (Stream & Tenant), canPushdown: x => x is not MetadataValueFilter);

        plan.Pushdown.ShouldBe(Name | Stream);
        plan.Residual.ShouldBe(Name | (Stream & Tenant));
    }

    [Test]
    public void Create_WhenFilterIsAllOrNone_PushesFilterWithoutChanges()
    {
        Plan(EventFilter.All, canPushdown: _ => false)
            .ShouldBe(new EventFilterPlan(EventFilter.All, EventFilter.All));

        Plan(EventFilter.None, canPushdown: _ => false)
            .ShouldBe(new EventFilterPlan(EventFilter.None, EventFilter.All));
    }

    [Test]
    public void Create_WhenPushdownModeIsNone_PushesNothing()
    {
        var plan = Plan(Name & Tenant, canPushdown: _ => true, FilterPushdownMode.None);

        plan.Pushdown.ShouldBe(EventFilter.All);
        plan.Residual.ShouldBe(Name & Tenant);
    }

    [Test]
    public void Create_WhenPushdownIsRequiredAndResidualRemains_Throws()
    {
        var exception = Should.Throw<EventFilterNotSupportedException>(() =>
            Plan(Name & Tenant, canPushdown: x => x is not MetadataValueFilter, FilterPushdownMode.Require));

        exception.Message.ShouldContain(Tenant.ToString());
    }

    [Test]
    public void Create_WhenPushdownIsRequiredAndNoResidualRemains_Succeeds()
    {
        Plan(Name & Tenant, canPushdown: _ => true, FilterPushdownMode.Require).Residual.ShouldBe(EventFilter.All);
    }

    [Test]
    public void Create_WhenFilterIsOfType_PushesEquivalentEventNames()
    {
        var plan = Plan(EventFilter.OfType<EventFilterUniverse.IOrderEvent>(), canPushdown: x => x is EventNameFilter);

        plan.Pushdown.ShouldBe(OrderNames);
        plan.Residual.ShouldBe(EventFilter.All);
    }

    [Test]
    public void Create_WhenNoEventNamesMatchType_PushesNone()
    {
        Plan(EventFilter.OfType<string>(), canPushdown: _ => true).Pushdown.ShouldBe(EventFilter.None);
    }

    [Test]
    public void Create_WhenTypeFilterHasPredicate_PushesEventNamesButLeavesPredicateAsResidual()
    {
        var predicate = EventFilter.OfType<EventFilterUniverse.Order>(e => e.Customer != null);

        var plan = Plan(Stream & predicate, canPushdown: x => x is not EventTypeFilter);

        plan.Pushdown.ShouldBe(Stream & OrderNames);
        plan.Residual.ShouldBe(predicate);
    }

    [Test]
    public void Create_WhenDisjunctionContainsTypePredicate_PushesEventNamesAndLeavesPredicateResidual()
    {
        var plan = Plan(IsLarge | Tenant, canPushdown: x => x is not EventTypeFilter);

        plan.Pushdown.ShouldBe(OrderNames | Tenant);
        plan.Residual.ShouldBe((OrderNames & IsLarge) | Tenant);
    }

    [Test]
    public void Create_WhenTypePredicateIsNegated_PredicateIsNotPushed()
    {
        Plan(Stream & !IsLarge, canPushdown: x => x is not EventTypeFilter).Pushdown.ShouldBe(Stream);
    }

    [Test]
    public void Create_WhenPushdownIsRequiredAndTypePredicateRemains_Throws()
    {
        Should.Throw<EventFilterNotSupportedException>(() =>
            Plan(IsLarge, canPushdown: x => x is not EventTypeFilter, FilterPushdownMode.Require));
    }

    [Test]
    public void IsMetadataRequired_WhenResidualContainsMetadataFilter_ReturnsTrue()
    {
        Plan(Name & !Tenant, canPushdown: x => x is EventNameFilter).IsMetadataRequired.ShouldBeTrue();
    }

    [Test]
    public void IsMetadataRequired_WhenResidualContainsNoMetadataFilter_ReturnsFalse()
    {
        Plan(Name & !Tenant, canPushdown: _ => true).IsMetadataRequired.ShouldBeFalse();
    }

    [Test]
    public void Create_WhenGivenRandomFilterAndPushableLeafTypes_PlanPreservesFilterSemantics()
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
    public void WithEventNames_WhenGivenRandomFilter_ResolvesTypeFiltersWithoutChangingSemantics()
    {
        var random = new Random(5);

        for (var i = 0; i < 500; i++)
        {
            var filter = EventFilterUniverse.NextFilter(random);
            var resolved = filter.WithEventNames(EventFilterUniverse.GetEventNames);

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
        typeof(CreatedAtFilter)
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
            canPushdown);
    }
}