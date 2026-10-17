namespace DomainBlocks.EventStore.Subscriptions;

/// <summary>
/// Provides the reads that subscriptions use to follow a store's event log as events are appended. A store implements
/// it to support subscriptions.
/// </summary>
/// <remarks>
/// <para>
/// The reads never wait for events to be appended, and keep no position: the caller tracks the position, and calls
/// <see cref="WaitForAppendAsync"/> once it has read every event. The caller creates a source each time it starts
/// following the log, and disposes it when it stops.
/// </para>
/// <para>
/// The caller makes one call at a time, so calls never overlap. A source that listens for appends in the background
/// must still synchronize its listener with <see cref="WaitForAppendAsync"/>.
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
    /// Waits until an event is appended, or for <paramref name="timeout"/> to elapse. An append while no wait is in
    /// progress completes the next wait immediately.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if an event was appended, or <see langword="false"/> if the timeout elapsed first.
    /// </returns>
    ValueTask<bool> WaitForAppendAsync(TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>
    /// Decides whether a failure of <see cref="ReadEndPositionAsync"/> or <see cref="ReadAsync"/> is transient. A
    /// transient failure is retried with backoff, and any other is not.
    /// </summary>
    bool IsTransientError(Exception exception);
}