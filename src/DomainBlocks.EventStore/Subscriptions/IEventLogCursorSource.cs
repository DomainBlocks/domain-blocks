namespace DomainBlocks.EventStore.Subscriptions;

/// <summary>
/// Provides the reads that subscriptions use to follow a store's event log as events are appended. A store implements
/// it to support subscriptions.
/// </summary>
/// <remarks>
/// <para>
/// The reads never wait for events to be appended, and keep no position: the caller tracks the position, and once it
/// has read every event, it waits until <see cref="ListenForAppendsAsync"/> yields a later position. The caller creates
/// a source each time it starts following the log, and disposes it when it stops.
/// </para>
/// <para>
/// The caller makes one read at a time, so the reads never overlap. It enumerates
/// <see cref="ListenForAppendsAsync"/> alongside them, and stops enumerating it before it disposes the source.
/// </para>
/// </remarks>
/// <typeparam name="TSourceEvent">An event as the store holds it, which is decoded only on request.</typeparam>
/// <typeparam name="TLogPos">The type of position in the event log.</typeparam>
public interface IEventLogCursorSource<TSourceEvent, TLogPos> : IAsyncDisposable where TLogPos : struct
{
    /// <summary>
    /// Reads the position of the last event in the log, or <see langword="null"/> if the log is empty.
    /// </summary>
    ValueTask<TLogPos?> ReadEndPositionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads up to <paramref name="maxCount"/> events after <paramref name="fromExclusive"/>, or from the start of the
    /// log if it is <see langword="null"/>, in log order. It returns at once, with no events if none have been
    /// appended. The events are valid only until the next call.
    /// </summary>
    ValueTask<IReadOnlyList<TSourceEvent>> ReadAsync(
        TLogPos? fromExclusive,
        int maxCount,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets the position of an event that this source has read.
    /// </summary>
    /// <remarks>
    /// The caller cannot read the position through <see cref="IReadEvent{TPayload,TStreamId,TStreamPos,TLogPos}"/>
    /// while the stream ID and position types are generic, because a constraint on <typeparamref name="TSourceEvent"/>
    /// would have to name all four of its type arguments. Revisit this when those types are fixed: the constraint then
    /// becomes short enough to replace this method.
    /// </remarks>
    TLogPos GetLogPosition(TSourceEvent sourceEvent);

    /// <summary>
    /// Listens for events appended to the log, and yields the position of the last event in each append that it hears
    /// of. It runs until it is canceled, or until listening fails.
    /// </summary>
    /// <remarks>
    /// What it yields only saves time: an append that it hears late or not at all costs at most one poll interval,
    /// never an event. So it needs no recovery of its own: when it fails or ends, the caller logs the failure and
    /// listens again, backing off while it keeps failing.
    /// </remarks>
    IAsyncEnumerable<TLogPos> ListenForAppendsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Decides whether a failure of <see cref="ReadEndPositionAsync"/> or <see cref="ReadAsync"/> is transient. A
    /// transient failure is retried with backoff, and any other is not.
    /// </summary>
    bool IsTransientError(Exception exception);
}