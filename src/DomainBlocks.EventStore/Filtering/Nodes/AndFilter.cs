using System.Collections.Immutable;
using System.Text;

namespace DomainBlocks.EventStore.Filtering.Nodes;

/// <summary>
/// Matches the events that every one of <see cref="Operands"/> matches.
/// </summary>
public sealed record AndFilter : EventFilter
{
    private readonly ImmutableArray<EventFilter> _operands;

    internal AndFilter(IEnumerable<EventFilter> operands)
    {
        _operands = CheapestFirst(operands);
    }

    public IReadOnlyList<EventFilter> Operands => _operands;

    internal override int Cost => _operands.Max(x => x.Cost);

    public override bool Matches(IFilterableEvent filterable)
    {
        return _operands.All(operand => operand.Matches(filterable));
    }

    protected override void WriteTo(StringBuilder text) => Write(text, "and", _operands);

    public bool Equals(AndFilter? other) => other is not null && _operands.SequenceEqual(other._operands);

    public override int GetHashCode() => GetHashCode(typeof(AndFilter), _operands);
}