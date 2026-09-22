namespace DomainBlocks.EventStore.Filtering;

public static class EventStoreFilterExtensions
{
    /// <summary>
    /// Explains how the store handles a filter, i.e., what it pushes down and what it evaluates after reading.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A predicate over an event remains in the residual because only the decoded event can determine whether it is
    /// true. The pushdown shows whether the store can narrow the read using the stored payload as well as the event
    /// names.
    /// </para>
    /// <para>
    /// This explains the plan without reading any events. A subscription catches up using the same plan, with its
    /// store's subscription filter added when one exists. With read transforms, the plan describes what the store
    /// reads; the transformed events are then tested against the original filter.
    /// </para>
    /// </remarks>
    /// <exception cref="NotSupportedException">The store does not support filter explanation.</exception>
    /// <exception cref="EventFilterNotSupportedException">
    /// The store cannot satisfy the requested pushdown mode.
    /// </exception>
    public static EventFilterPlan ExplainFilter<TEvent, TStreamId, TStreamPos, TLogPos>(
        this IEventStore<TEvent, TStreamId, TStreamPos, TLogPos> store,
        EventFilter filter,
        FilterPushdownMode pushdownMode = FilterPushdownMode.Prefer)
        where TEvent : notnull
        where TStreamId : notnull
        where TStreamPos : notnull
        where TLogPos : notnull
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(filter);

        return store is IEventFilterExplainer explainer
            ? explainer.ExplainFilter(filter, pushdownMode)
            : throw new NotSupportedException(
                $"The store '{store.GetType().Name}' does not support filter explanation.");
    }
}