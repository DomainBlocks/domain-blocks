namespace DomainBlocks.EventStore.Filtering.Nodes;

/// <summary>
/// Represents a filter that matches events that <see cref="Operand"/> does not match.
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