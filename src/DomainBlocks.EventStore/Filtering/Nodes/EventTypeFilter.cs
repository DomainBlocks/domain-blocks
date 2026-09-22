using System.Linq.Expressions;
using System.Text;

namespace DomainBlocks.EventStore.Filtering.Nodes;

/// <summary>
/// Matches events that are read as <see cref="EventType"/>, or as a type derived from it, and that
/// <see cref="Predicate"/> is true of, if there is one. It is the one filter that needs the event decoded.
/// </summary>
/// <remarks>
/// Two of these with a predicate are equal only if they hold the same expression, not merely an equivalent one.
/// </remarks>
public sealed record EventTypeFilter : EventFilter
{
    private readonly Lazy<Func<object, bool>>? _predicate;

    internal EventTypeFilter(Type eventType, LambdaExpression? predicate, Func<Func<object, bool>>? compile)
    {
        EventType = eventType;
        Predicate = predicate;
        _predicate = compile is null ? null : new Lazy<Func<object, bool>>(compile);
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