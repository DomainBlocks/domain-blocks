using System.Collections.Immutable;

namespace DomainBlocks.EventStore.Filtering.Nodes;

/// <summary>
/// Matches events whose name is one of <see cref="Names"/>.
/// </summary>
public sealed class EventNameFilter : EventFilter
{
    private readonly StringSet _names;

    internal EventNameFilter(StringSet names)
    {
        _names = names;
    }

    /// <summary>
    /// The names, distinct and in ordinal order.
    /// </summary>
    public ImmutableArray<string> Names => _names.Values;

    public override bool Matches(IFilterableEvent filterable) => _names.Contains(filterable.EventName);

    public override string ToString() => Format(nameof(EventNames), Names);
}