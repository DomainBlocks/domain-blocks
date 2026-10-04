namespace DomainBlocks.EventStore.Filtering.Nodes;

/// <summary>
/// Matches no event.
/// </summary>
public sealed class NoEventsFilter : EventFilter
{
    internal NoEventsFilter()
    {
    }

    public override bool Matches(IFilterableEvent filterable) => false;

    public override string ToString() => nameof(None);
}