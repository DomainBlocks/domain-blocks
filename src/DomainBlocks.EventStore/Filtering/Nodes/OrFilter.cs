using System.Collections.Immutable;

namespace DomainBlocks.EventStore.Filtering.Nodes;

/// <summary>
/// Matches events that match at least one filter in <see cref="Operands"/>.
/// </summary>
public sealed class OrFilter : EventFilter
{
    internal OrFilter(ImmutableArray<EventFilter> operands)
    {
        Operands = operands;
    }

    /// <summary>
    /// The filters, in the order they were combined. They are evaluated in this order until one matches.
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