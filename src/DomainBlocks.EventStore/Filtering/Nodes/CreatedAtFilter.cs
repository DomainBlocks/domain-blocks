namespace DomainBlocks.EventStore.Filtering.Nodes;

/// <summary>
/// Represents a filter that matches events created at or after <see cref="From"/> and before <see cref="Before"/>. A
/// <see langword="null"/> bound leaves that side open.
/// </summary>
public sealed class CreatedAtFilter : EventFilter
{
    internal CreatedAtFilter(DateTimeOffset? from, DateTimeOffset? before)
    {
        From = from;
        Before = before;
    }

    public DateTimeOffset? From { get; }

    public DateTimeOffset? Before { get; }

    public override bool Matches(IFilterableEvent filterable)
    {
        var createdAt = filterable.CreatedAt;

        return (From is null || createdAt >= From) && (Before is null || createdAt < Before);
    }

    public override string ToString()
    {
        return (From, Before) switch
        {
            ({ } from, null) => $"{nameof(CreatedAtOrAfter)}({Format(from)})",
            (null, { } before) => $"{nameof(CreatedBefore)}({Format(before)})",
            ({ } from, { } before) =>
                $"({nameof(CreatedAtOrAfter)}({Format(from)}) & {nameof(CreatedBefore)}({Format(before)}))",
            _ => nameof(All)
        };
    }
}