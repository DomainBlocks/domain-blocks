using System.Text;

namespace DomainBlocks.EventStore.Filtering.Nodes;

public sealed record AllEventsFilter : EventFilter
{
    internal AllEventsFilter()
    {
    }

    public override bool Matches(IFilterableEvent filterable) => true;

    protected override void WriteTo(StringBuilder text) => text.Append("all");
}