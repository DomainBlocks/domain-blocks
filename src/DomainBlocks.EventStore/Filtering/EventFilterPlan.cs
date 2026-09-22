using System.Reflection;
using DomainBlocks.EventStore.Filtering.Nodes;

namespace DomainBlocks.EventStore.Filtering;

/// <summary>
/// A filter pushdown plan, i.e., the part pushed to the native query and the residual part evaluated after reading.
/// </summary>
public sealed record EventFilterPlan
{
    /// <summary>
    /// A plan for an unfiltered read.
    /// </summary>
    public static readonly EventFilterPlan Unfiltered = new(EventFilter.All, EventFilter.All);

    internal EventFilterPlan(EventFilter pushdown, EventFilter residual)
    {
        Pushdown = pushdown;
        Residual = residual;
        IsMetadataRequired = residual.GetLeafNodes().Any(x => x is MetadataExistsFilter or MetadataValueFilter);
    }

    /// <summary>
    /// The filter used for pushdown. It must not rule out any event matched by the original filter.
    /// </summary>
    public EventFilter Pushdown { get; }

    /// <summary>
    /// The residual filter to be evaluated after events are read.
    /// </summary>
    public EventFilter Residual { get; }

    /// <summary>
    /// Whether the residual filter requires event metadata.
    /// </summary>
    public bool IsMetadataRequired { get; }

    /// <summary>
    /// Whether pushdown narrows the read using stored payload values.
    /// </summary>
    public bool IsNarrowedByPayload => Pushdown.GetLeafNodes().Any(x => x is PayloadValueFilter);

    /// <summary>
    /// Creates a filter pushdown plan for the specified filter.
    /// </summary>
    /// <param name="filter">The filter to plan.</param>
    /// <param name="pushdownMode">How much of the filter must be pushed down to the native query.</param>
    /// <param name="eventNamesResolver">Resolves the names stored for an event type.</param>
    /// <param name="canPushdown">Whether the database can push down a leaf filter.</param>
    /// <param name="storedPathResolver">Resolves where an event member is stored in the payload.</param>
    /// <exception cref="EventFilterNotSupportedException">
    /// <paramref name="pushdownMode"/> is <see cref="FilterPushdownMode.Require"/> and part of the filter is cannot be
    /// pushed down.
    /// </exception>
    public static EventFilterPlan Create(
        EventFilter filter,
        FilterPushdownMode pushdownMode,
        Func<Type, IReadOnlyCollection<string>> eventNamesResolver,
        Func<EventFilter, bool> canPushdown,
        Func<Type, IReadOnlyList<MemberInfo>, string?>? storedPathResolver = null)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(canPushdown);

        if (filter is AllEventsFilter)
            return Unfiltered;

        var resolved = filter.LowerEventTypes(eventNamesResolver);

        if (pushdownMode == FilterPushdownMode.None)
            return new EventFilterPlan(EventFilter.All, resolved);

        var pushdown = EventFilter.All;
        var residual = EventFilter.All;

        // Push each conjunct independently. If it cannot be fully pushed down, weaken it and keep it residual.
        foreach (var conjunct in resolved is AndFilter conjunction ? conjunction.Operands : [resolved])
        {
            if (conjunct.GetLeafNodes().All(CanPushdown))
            {
                pushdown &= conjunct;
            }
            else
            {
                pushdown &= Weaken(conjunct);
                residual &= conjunct;
            }
        }

        if (pushdownMode == FilterPushdownMode.Require && residual is not AllEventsFilter)
        {
            throw new EventFilterNotSupportedException(
                $"The filter '{filter}' cannot be evaluated by the database alone, which {nameof(FilterPushdownMode)}." +
                $"{nameof(FilterPushdownMode.Require)} asks for. This part of it is left over: '{residual}'.");
        }

        return new EventFilterPlan(pushdown, residual);

        bool CanPushdown(EventFilter leaf) => leaf is AllEventsFilter or NoEventsFilter || canPushdown(leaf);

        // Weaken the pushdown without ruling out any event matched by the original filter.
        EventFilter Weaken(EventFilter conjunct) => conjunct.Rewrite((leaf, isNegated) =>
            CanPushdown(leaf) ? leaf : isNegated ? EventFilter.None : Lower(leaf));

        // Use stored payload values to narrow the pushdown when they can rule out events that contradict the predicate.
        EventFilter Lower(EventFilter leaf)
        {
            if (leaf is not EventTypeFilter { Predicate: { } predicate } byType || storedPathResolver is null)
                return EventFilter.All;

            var lowered = PredicateLowering.Lower(predicate, members => storedPathResolver(byType.EventType, members));

            return lowered.GetLeafNodes().All(CanPushdown) ? lowered : EventFilter.All;
        }
    }

    public override string ToString() => $"pushdown: {Pushdown}; residual: {Residual}";
}