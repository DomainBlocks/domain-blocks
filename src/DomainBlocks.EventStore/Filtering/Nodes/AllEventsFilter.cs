namespace DomainBlocks.EventStore.Filtering.Nodes;

/// <summary>
/// Represents a filter that matches every event.
/// </summary>
public sealed class AllEventsFilter : EventFilter
{
    internal AllEventsFilter()
    {
    }

    public override bool Matches(IFilterableEvent filterable) => true;

    public override string ToString() => nameof(All);
}