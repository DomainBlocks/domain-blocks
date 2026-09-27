using System.Collections.Immutable;
using System.Text;

namespace DomainBlocks.EventStore.Filtering.Nodes;

/// <summary>
/// Matches the events that any one of <see cref="Operands"/> matches.
/// </summary>
public sealed record OrFilter : EventFilter
{
    private readonly ImmutableArray<EventFilter> _operands;

    internal OrFilter(IEnumerable<EventFilter> operands)
    {
        _operands = CheapestFirst(operands);
    }

    public IReadOnlyList<EventFilter> Operands => _operands;

    internal override int Cost => _operands.Max(x => x.Cost);

    public override bool Matches(IFilterableEvent filterable)
    {
        return _operands.Any(operand => operand.Matches(filterable));
    }

    protected override void WriteTo(StringBuilder text) => Write(text, "or", _operands);

    public bool Equals(OrFilter? other) => other is not null && _operands.SequenceEqual(other._operands);

    public override int GetHashCode() => GetHashCode(typeof(OrFilter), _operands);
}