using Polly;

namespace DomainBlocks.EventStore.Internal.Subscriptions;

/// <summary>
/// Represents what a subscription replays: the followed sequence from an origin up to an end that was read. Every read
/// returns the same events, because the log is append-only and the reads stop at the end.
/// </summary>
/// <remarks>
/// A store derives from this class to supply its reads, and overrides <see cref="DisposeAsyncCore"/> if a snapshot holds
/// a resource for them, such as a session. So a caller reads a snapshot one read at a time, and not after disposing it.
/// </remarks>
internal abstract class CatchUp<TEvent, TPos> : IAsyncDisposable
    where TEvent : notnull
    where TPos : struct, IPosition<TPos>
{
    private readonly ResiliencePipeline _resiliencePipeline;
    private readonly long _start;
    private readonly long _end;

    /// <param name="origin">Where the subscription starts in the followed sequence.</param>
    /// <param name="end">
    /// The position of the sequence's last event, or <see langword="null"/> if it has no events.
    /// </param>
    /// <param name="resiliencePipeline">The policy that retries each read.</param>
    protected CatchUp(SubscriptionOrigin<TPos> origin, long? end, ResiliencePipeline resiliencePipeline)
    {
        _resiliencePipeline = resiliencePipeline;

        // -1 is the position before the first event, where an empty sequence ends.
        _end = end ?? -1;

        _start = origin switch
        {
            SubscriptionOrigin<TPos>.After after => after.Position.ToInt64(),
            SubscriptionOrigin<TPos>.End => _end,
            _ => -1
        };

        var endPosition = Math.Max(_start, _end);
        EndPosition = endPosition < 0 ? null : TPos.FromInt64(endPosition);
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
    public TPos? EndPosition { get; }

    /// <summary>
    /// Reads the events from the origin up to the end. After a transient error, it reads again after the last event
    /// that the caller took. The sequence is empty if there are no events to read.
    /// </summary>
    public IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadAll()
    {
        return ResumableAsyncEnumerable.Create(
            static (s, start, ct) => s.ReadAfterAsync(start, ct),
            static (_, e) => TPos.FromContext(e.Context).ToInt64(),
            this,
            _start,
            _resiliencePipeline);
    }

    public ValueTask DisposeAsync() => DisposeAsyncCore();

    /// <summary>
    /// Reads the followed sequence from one position to another. A start of -1 reads from the start of the sequence.
    /// </summary>
    protected abstract IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadAsync(
        long startExclusive,
        long endInclusive,
        CancellationToken cancellationToken);

    /// <summary>
    /// Releases what the snapshot holds for its reads. By default, it holds nothing.
    /// </summary>
    protected virtual ValueTask DisposeAsyncCore() => ValueTask.CompletedTask;

    private IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadAfterAsync(
        long startExclusive,
        CancellationToken cancellationToken)
    {
        return startExclusive < _end
            ? ReadAsync(startExclusive, _end, cancellationToken)
            : AsyncEnumerable.Empty<ReadEvent<TEvent, string, StreamPosition, LogPosition>>();
    }
}