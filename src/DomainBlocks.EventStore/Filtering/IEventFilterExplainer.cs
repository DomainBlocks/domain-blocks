namespace DomainBlocks.EventStore.Filtering;

/// <summary>
/// Implemented by a store that can explain how it handles a filter, including what it pushes down and what it evaluates
/// after reading.
/// </summary>
public interface IEventFilterExplainer
{
    /// <summary>
    /// Explains how the store would read with the specified filter, without reading any events.
    /// </summary>
    /// <exception cref="EventFilterNotSupportedException"> The store cannot satisfy the requested pushdown mode.
    /// </exception>
    EventFilterPlan ExplainFilter(EventFilter filter, FilterPushdownMode pushdownMode = FilterPushdownMode.Prefer);
}