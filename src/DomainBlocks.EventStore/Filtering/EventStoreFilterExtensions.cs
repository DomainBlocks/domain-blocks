namespace DomainBlocks.EventStore.Filtering;

public static class EventStoreFilterExtensions
{
    /// <summary>
    /// Explains how the store handles a filter, including what it pushes down and what it evaluates after reading
    /// events.
    /// </summary>
    /// <remarks>
    /// Type predicates remain in the residual because they require the decoded event. The pushdown narrows the read to
    /// the corresponding event names.
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