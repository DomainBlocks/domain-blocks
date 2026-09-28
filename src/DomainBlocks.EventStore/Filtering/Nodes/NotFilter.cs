using System.Text;

namespace DomainBlocks.EventStore.Filtering.Nodes;

/// <summary>
/// Matches events that do not match <see cref="Operand"/>.
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