namespace DomainBlocks.EventStore.Internal.Subscriptions;

/// <summary>
/// Represents a followed sequence from an origin up to the end that was read, which every read of the snapshot sees the
/// same way. It is not a snapshot in the database's sense: the log is append-only and the reads stop at the end, so
/// what they return can't change.
/// </summary>
/// <remarks>
/// A store derives from this class to supply its read of the sequence. The rules for what each origin reads are here.
/// </remarks>
internal abstract class CatchUpSnapshot<TEvent, TPos>
    where TEvent : notnull
    where TPos : struct, IPosition<TPos>
{
    private readonly long _start;
    private readonly long _end;

    protected CatchUpSnapshot(SubscriptionOrigin<TPos> origin, long? end)
    {
        // -1 is the position before the first event, where an empty sequence ends.
        _end = end ?? -1;

        _start = origin switch
        {
            SubscriptionOrigin<TPos>.After after => after.Position.ToInt64(),
            SubscriptionOrigin<TPos>.End => _end,
            _ => -1
        };

        var position = Math.Max(_start, _end);
        Position = position < 0 ? null : TPos.FromInt64(position);
    }

    /// <summary>
    /// Gets the position that a subscription has reached once it has read the snapshot, or <see langword="null"/> if it
    /// is still before the first event.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A read covers the sequence up to the end, even if the filter selected none of its events, so a later read does
    /// not need that part of the sequence again.
    /// </para>
    /// <para>
    /// For a subscription from the end, this pins the end. Otherwise, a restart before the first event is delivered
    /// would resume from a later end and skip the events appended in between.
    /// </para>
    /// </remarks>
    public TPos? Position { get; }

    /// <summary>
    /// Reads the events up to the end, from the origin, or after the given position to resume a read that failed. The
    /// sequence is empty if there are no events to read.
    /// </summary>
    public IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadAsync(
        TPos? after,
        CancellationToken cancellationToken)
    {
        var start = after?.ToInt64() ?? _start;

        return start < _end
            ? ReadCoreAsync(start, _end, cancellationToken)
            : AsyncEnumerable.Empty<ReadEvent<TEvent, string, StreamPosition, LogPosition>>();
    }

    /// <summary>
    /// Reads the followed sequence from one position to another. A start of -1 reads from the start of the sequence.
    /// </summary>
    protected abstract IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadCoreAsync(
        long startExclusive,
        long endInclusive,
        CancellationToken cancellationToken);
}