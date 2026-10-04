namespace DomainBlocks.EventStore.Filtering.Nodes;

/// <summary>
/// Matches events that do not match <see cref="Operand"/>.
/// </summary>
public sealed class NotFilter : EventFilter
{
    internal NotFilter(EventFilter operand)
    {
        Operand = operand;
    }

    public EventFilter Operand { get; }

    public override bool Matches(IFilterableEvent filterable) => !Operand.Matches(filterable);

    public override string ToString() => $"!{Operand}";
}