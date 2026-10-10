namespace DomainBlocks.EventStore.Filtering.Nodes;

/// <summary>
/// Represents a filter that matches events whose stream ID starts with <see cref="Prefix"/>, compared ordinally.
/// </summary>
public sealed class StreamIdPrefixFilter : EventFilter
{
    internal StreamIdPrefixFilter(string prefix)
    {
        Prefix = prefix;
    }

    public string Prefix { get; }

    public override bool Matches(IFilterableEvent filterable) =>
        filterable.StreamId.StartsWith(Prefix, StringComparison.Ordinal);

    public override string ToString() => Format(nameof(StreamIdStartsWith), [Prefix]);
}