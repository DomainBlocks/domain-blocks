using System.Text;

namespace DomainBlocks.EventStore.Filtering.Nodes;

/// <summary>
/// Matches the events that <see cref="Operand"/> does not.
/// </summary>
public sealed record NotFilter : EventFilter
{
    internal NotFilter(EventFilter operand)
    {
        Operand = operand;
    }

    public EventFilter Operand { get; }

    internal override int Cost => Operand.Cost;

    public override bool Matches(IFilterableEvent filterable) => !Operand.Matches(filterable);

    protected override void WriteTo(StringBuilder text) => Write(text, "not", [Operand]);
}