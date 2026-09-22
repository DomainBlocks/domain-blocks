namespace DomainBlocks.EventStore.Filtering;

/// <summary>
/// Controls how much of a filter the store should push down to its native query.
/// </summary>
public enum FilterPushdownMode
{
    /// <summary>
    /// Push down as much as the store can evaluate. The residual is tested against each event after it is read.
    /// </summary>
    Prefer,

    /// <summary>
    /// Require the entire filter to be pushed down. A filter with a residual is refused.
    /// </summary>
    Require,

    /// <summary>
    /// Do not push down the filter. Every event is read and tested against it.
    /// </summary>
    None
}