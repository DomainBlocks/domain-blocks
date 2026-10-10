using System.Collections.Immutable;

namespace DomainBlocks.EventStore.Filtering.Nodes;

/// <summary>
/// Represents a filter that matches events that match any of <see cref="Operands"/>.
/// </summary>
public sealed class OrFilter : EventFilter
{
    internal OrFilter(ImmutableArray<EventFilter> operands)
    {
        Operands = operands;
    }

    /// <summary>
    /// Gets the filters, in the order they were combined. <see cref="Matches"/> evaluates them in this order and stops
    /// at the first one that matches.
    /// </summary>
    public ImmutableArray<EventFilter> Operands { get; }

    public override bool Matches(IFilterableEvent filterable)
    {
        foreach (var operand in Operands)
        {
            if (operand.Matches(filterable))
                return true;
        }

        return false;
    }

    public override string ToString() => $"({string.Join(" | ", Operands)})";
}