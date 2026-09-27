using System.Text;

namespace DomainBlocks.EventStore.Filtering.Nodes;

public sealed record NoEventsFilter : EventFilter
{
    internal NoEventsFilter()
    {
    }

    public override bool Matches(IFilterableEvent filterable) => false;

    protected override void WriteTo(StringBuilder text) => text.Append("none");
}