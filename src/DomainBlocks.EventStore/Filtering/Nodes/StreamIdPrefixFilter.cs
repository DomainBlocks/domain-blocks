using System.Text;

namespace DomainBlocks.EventStore.Filtering.Nodes;

/// <summary>
/// Matches events whose stream ID starts with <see cref="Prefix"/>, using an ordinal comparison.
/// </summary>
public sealed record StreamIdPrefixFilter : EventFilter
{
    internal StreamIdPrefixFilter(string prefix)
    {
        Prefix = prefix;
    }

    public string Prefix { get; }

    public override bool Matches(IFilterableEvent filterable) =>
        filterable.StreamId.StartsWith(Prefix, StringComparison.Ordinal);

    protected override void WriteTo(StringBuilder text) => Write(text, "streamIdPrefix", [Prefix]);
}