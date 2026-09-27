using System.Text;

namespace DomainBlocks.EventStore.Filtering.Nodes;

/// <summary>
/// Matches events in any of the streams <see cref="Ids"/>.
/// </summary>
public sealed record StreamIdFilter : EventFilter
{
    private readonly StringSet _ids;

    internal StreamIdFilter(StringSet ids)
    {
        _ids = ids;
    }

    public IReadOnlyList<string> Ids => _ids.Values;

    public override bool Matches(IFilterableEvent filterable) => _ids.Contains(filterable.StreamId);

    protected override void WriteTo(StringBuilder text) => Write(text, "streamId", _ids.Values);
}