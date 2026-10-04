using System.Collections.Immutable;

namespace DomainBlocks.EventStore.Filtering.Nodes;

/// <summary>
/// Matches events that match every filter in <see cref="Operands"/>.
/// </summary>
public sealed class AndFilter : EventFilter
{
    internal AndFilter(ImmutableArray<EventFilter> operands)
    {
        Operands = operands;
    }

    /// <summary>
    /// The filters, in the order they were combined. They are evaluated in this order until one does not match.
    /// </summary>
    public ImmutableArray<EventFilter> Operands { get; }

    public override bool Matches(IFilterableEvent filterable)
    {
        foreach (var operand in Operands)
        {
            if (!operand.Matches(filterable))
                return false;
        }

        return true;
    }

    public override string ToString() => $"({string.Join(" & ", Operands)})";
}