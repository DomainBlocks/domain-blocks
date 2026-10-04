namespace DomainBlocks.EventStore.Filtering.Nodes;

/// <summary>
/// Matches every event.
/// </summary>
public sealed class AllEventsFilter : EventFilter
{
    internal AllEventsFilter()
    {
    }

    public override bool Matches(IFilterableEvent filterable) => true;
}