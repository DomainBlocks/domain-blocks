using System.Text;

namespace DomainBlocks.EventStore.Filtering.Nodes;

/// <summary>
/// Matches events in the half-open interval <c>[From, Before)</c>. A <see langword="null"/> bound makes that side
/// unbounded.
/// </summary>
public sealed record CreatedAtFilter : EventFilter
{
    internal CreatedAtFilter(DateTimeOffset? from, DateTimeOffset? before)
    {
        From = from;
        Before = before;
    }

    public DateTimeOffset? From { get; }

    public DateTimeOffset? Before { get; }

    public override bool Matches(IFilterableEvent filterable) =>
        (From is null || filterable.CreatedAt >= From) && (Before is null || filterable.CreatedAt < Before);

    protected override void WriteTo(StringBuilder text) =>
        text.Append("createdAt(").Append(Write(From)).Append(',').Append(Write(Before)).Append(')');
}