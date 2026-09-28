using System.Linq.Expressions;
using System.Text;

namespace DomainBlocks.EventStore.Filtering.Nodes;

/// <summary>
/// Matches events whose decoded payload is assignable to <see cref="EventType"/> and, if present, satisfies
/// <see cref="Predicate"/>.
/// </summary>
public sealed record EventTypeFilter : EventFilter
{
    private readonly Lazy<Func<object, bool>>? _predicate;

    internal EventTypeFilter(Type eventType, LambdaExpression? predicate, Func<Func<object, bool>>? compiler)
    {
        EventType = eventType;
        Predicate = predicate;
        _predicate = compiler is null ? null : new Lazy<Func<object, bool>>(compiler);
    }

    public Type EventType { get; }

    public LambdaExpression? Predicate { get; }

    internal override int Cost => 2;

    public override bool Matches(IFilterableEvent filterable)
    {
        if (!EventType.IsInstanceOfType(filterable.DecodedPayload))
            return false;

        try
        {
            return _predicate is null || _predicate.Value(filterable.DecodedPayload);
        }
        catch (NullReferenceException)
        {
            // Treat a null encountered while evaluating the predicate as a non-match.
            return false;
        }
    }

    // Predicate text is diagnostic only and not intended to be a stable identifier.
    protected override void WriteTo(StringBuilder text) =>
        Write(
            text,
            "eventType",
            Predicate is null ? [EventType.ToString()] : [EventType.ToString(), Predicate.ToString()]);

    public bool Equals(EventTypeFilter? other) =>
        other is not null && EventType == other.EventType && ReferenceEquals(Predicate, other.Predicate);

    public override int GetHashCode() => HashCode.Combine(EventType, Predicate);
}