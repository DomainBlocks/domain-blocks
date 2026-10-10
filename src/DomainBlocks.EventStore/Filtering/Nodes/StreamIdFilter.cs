using System.Collections.Immutable;

namespace DomainBlocks.EventStore.Filtering.Nodes;

/// <summary>
/// Represents a filter that matches events whose stream ID is one of <see cref="Ids"/>.
/// </summary>
public sealed class StreamIdFilter : EventFilter
{
    private readonly StringSet _ids;

    internal StreamIdFilter(StringSet ids)
    {
        _ids = ids;
    }

    /// <summary>
    /// Gets the stream IDs, distinct and in ordinal order.
    /// </summary>
    public ImmutableArray<string> Ids => _ids.Values;

    public override bool Matches(IFilterableEvent filterable) => _ids.Contains(filterable.StreamId);

    public override string ToString() => Format(nameof(StreamIds), Ids);
}