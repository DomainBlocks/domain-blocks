namespace DomainBlocks.EventStore.Filtering.Nodes;

/// <summary>
/// Represents a filter that matches no events.
/// </summary>
public sealed class NoEventsFilter : EventFilter
{
    internal NoEventsFilter()
    {
    }

    public override bool Matches(IFilterableEvent filterable) => false;

    public override string ToString() => nameof(None);
}