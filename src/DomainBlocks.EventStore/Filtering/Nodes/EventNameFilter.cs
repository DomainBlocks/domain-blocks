using System.Text;

namespace DomainBlocks.EventStore.Filtering.Nodes;

/// <summary>
/// Matches events stored under any of <see cref="Names"/>.
/// </summary>
public sealed record EventNameFilter : EventFilter
{
    private readonly StringSet _names;

    internal EventNameFilter(StringSet names)
    {
        _names = names;
    }

    public IReadOnlyList<string> Names => _names.Values;

    public override bool Matches(IFilterableEvent filterable) => _names.Contains(filterable.EventName);

    protected override void WriteTo(StringBuilder text) => Write(text, "eventName", _names.Values);
}