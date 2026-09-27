namespace DomainBlocks.EventStore.Filtering;

/// <summary>
/// Implemented by a store that can explain how it handles a filter, i.e., what it pushes down and what it evaluates
/// after reading. See <see cref="EventStoreFilterExtensions"/>.
/// </summary>
public interface IEventFilterExplainer
{
    /// <summary>
    /// Explains how the store would carry out a read with the filter. Nothing is read.
    /// </summary>
    /// <exception cref="EventFilterNotSupportedException">
    /// The store cannot satisfy the requested pushdown mode.
    /// </exception>
    EventFilterPlan ExplainFilter(EventFilter filter, FilterPushdownMode pushdownMode = FilterPushdownMode.Prefer);
}