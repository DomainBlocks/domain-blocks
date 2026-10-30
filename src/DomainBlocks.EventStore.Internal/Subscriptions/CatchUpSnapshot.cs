using Polly;

namespace DomainBlocks.EventStore.Internal.Subscriptions;

/// <summary>
/// Represents what a subscription replays from a snapshot of a store's history: the followed sequence from an origin up
/// to the end. The rules for what each origin reads are here.
/// </summary>
internal sealed class CatchUpSnapshot<TEvent, TPos>
    where TEvent : notnull
    where TPos : struct, IPosition<TPos>
{
    private readonly IHistorySnapshot<TEvent> _snapshot;
    private readonly Func<ReadEventContext<string, StreamPosition, LogPosition>, TPos> _positionSelector;
    private readonly ResiliencePipeline _retryPipeline;
    private readonly long _start;
    private readonly long _end;

    public CatchUpSnapshot(
        IHistorySnapshot<TEvent> snapshot,
        SubscriptionOrigin<TPos> origin,
        Func<ReadEventContext<string, StreamPosition, LogPosition>, TPos> positionSelector,
        ResiliencePipeline retryPipeline)
    {
        _snapshot = snapshot;
        _positionSelector = positionSelector;
        _retryPipeline = retryPipeline;

        // -1 is the position before the first event, where an empty sequence ends.
        _end = snapshot.End ?? -1;

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
    /// Reads the events from the origin up to the end. After a transient error, it reads again after the last event
    /// that the caller took. The sequence is empty if there are no events to read.
    /// </summary>
    public IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadAsync()
    {
        return ResumableAsyncEnumerable.Create(
            static (s, start, ct) => s.ReadAfterAsync(start, ct),
            static (s, e) => s._positionSelector(e.Context).ToInt64(),
            this,
            _start,
            _retryPipeline);
    }

    private IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadAfterAsync(
        long startExclusive,
        CancellationToken cancellationToken)
    {
        return startExclusive < _end
            ? _snapshot.ReadAsync(startExclusive, _end, cancellationToken)
            : AsyncEnumerable.Empty<ReadEvent<TEvent, string, StreamPosition, LogPosition>>();
    }
}