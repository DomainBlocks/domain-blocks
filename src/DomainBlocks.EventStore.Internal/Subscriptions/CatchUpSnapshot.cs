namespace DomainBlocks.EventStore.Internal.Subscriptions;

/// <summary>
/// Represents a followed sequence up to the end that was read, which every read of the snapshot sees the same way. It
/// is not a snapshot in the database's sense: the log is append-only and the reads stop at the end, so what they return
/// can't change.
/// </summary>
/// <remarks>
/// A store derives from this class to supply its read of the sequence. The rules for what each origin reads are here.
/// </remarks>
internal abstract class CatchUpSnapshot<TEvent, TPos>(long? end)
    where TEvent : notnull
    where TPos : struct, IPosition<TPos>
{
    /// <summary>
    /// Reads the events after the origin, up to the end. The sequence is empty if the origin is at or after the end.
    /// </summary>
    public IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadAsync(
        SubscriptionOrigin<TPos> origin,
        CancellationToken cancellationToken)
    {
        var startExclusive = origin is SubscriptionOrigin<TPos>.After after ? after.Position.ToInt64() : -1;

        if (end is { } endInclusive && origin is not SubscriptionOrigin<TPos>.End && startExclusive < endInclusive)
            return ReadCoreAsync(startExclusive, endInclusive, cancellationToken);

        return AsyncEnumerable.Empty<ReadEvent<TEvent, string, StreamPosition, LogPosition>>();
    }

    /// <summary>
    /// Gets the position to resume after once the events after the origin have been read, or <see langword="null"/> to
    /// resume from the start.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A read covers the sequence up to the end, even if the filter selected none of its events, so a later read does
    /// not need that part of the sequence again.
    /// </para>
    /// <para>
    /// For a subscription from the end, this pins the end. Otherwise, a restart before the first event is delivered
    /// would resume from a later end and skip the events appended in between. If the sequence is empty, the
    /// subscription resumes from the start.
    /// </para>
    /// </remarks>
    public TPos? GetResumePosition(SubscriptionOrigin<TPos> origin)
    {
        var after = origin is SubscriptionOrigin<TPos>.After a ? a.Position : (TPos?)null;

        if (end is not { } endInclusive)
            return after;

        return after is { } pos && pos.ToInt64() >= endInclusive ? pos : TPos.FromInt64(endInclusive);
    }

    /// <summary>
    /// Reads the followed sequence from one position to another. A start of -1 reads from the start of the sequence.
    /// </summary>
    protected abstract IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadCoreAsync(
        long startExclusive,
        long endInclusive,
        CancellationToken cancellationToken);
}