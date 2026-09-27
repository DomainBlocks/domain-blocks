using System.Linq.Expressions;
using System.Text;

namespace DomainBlocks.EventStore.Filtering.Nodes;

/// <summary>
/// Matches events that are read as a type assignable to <see cref="EventType"/>, and that <see cref="Predicate"/> is
/// true of (if not null). Requires the decoded payload.
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

    internal override int Cost => 3;

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
            // The predicate needed a value where the event has none. A filter either matches or it does not.
            return false;
        }
    }

    // The text of a predicate is for reading. It can differ from one build to the next.
    protected override void WriteTo(StringBuilder text) =>
        Write(
            text,
            "eventType",
            Predicate is null ? [EventType.ToString()] : [EventType.ToString(), Predicate.ToString()]);

    public bool Equals(EventTypeFilter? other) =>
        other is not null && EventType == other.EventType && ReferenceEquals(Predicate, other.Predicate);

    public override int GetHashCode() => HashCode.Combine(EventType, Predicate);
}