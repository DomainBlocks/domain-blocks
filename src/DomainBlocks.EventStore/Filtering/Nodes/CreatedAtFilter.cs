namespace DomainBlocks.EventStore.Filtering.Nodes;

/// <summary>
/// Matches events created in the half-open interval from <see cref="From"/> (inclusive) to <see cref="Before"/>
/// (exclusive). A <see langword="null"/> bound leaves that side of the interval open.
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
}