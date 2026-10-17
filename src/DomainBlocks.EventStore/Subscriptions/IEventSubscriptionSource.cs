using DomainBlocks.EventStore.Filtering;

namespace DomainBlocks.EventStore.Subscriptions;

/// <summary>
/// Provides the reads that subscriptions are built on: reads of the event log and of a stream, the end positions they
/// read up to, and the appends that the store hears of. A store implements it to support subscriptions.
/// </summary>
/// <remarks>
/// <para>
/// The reads never wait for events to be appended, and keep no position: the caller tracks the position, and once it
/// has read every event, it waits until <see cref="ListenForAppendsAsync"/> yields a later position.
/// </para>
/// <para>
/// The caller creates a source for each of its readers: one for the read of the log that live subscriptions share, and
/// one for each catch-up. It makes one read at a time on a source, so the reads never overlap. It enumerates
/// <see cref="ListenForAppendsAsync"/> only on the source of the shared read, alongside its reads, and stops
/// enumerating it before it disposes the source.
/// </para>
/// <para>
/// A read, including a read of an end position, must see every event that another of the store's sources has already
/// returned. Without that, a catch-up that begins after a live read could miss an event that the live read delivered.
/// </para>
/// </remarks>
/// <typeparam name="TSourceEvent">An event as the store holds it, which is decoded only on request.</typeparam>
/// <typeparam name="TStreamId">The type of stream ID.</typeparam>
/// <typeparam name="TStreamPos">The type of position in a stream.</typeparam>
/// <typeparam name="TLogPos">The type of position in the event log.</typeparam>
public interface IEventSubscriptionSource<TSourceEvent, TStreamId, TStreamPos, TLogPos> : IAsyncDisposable
    where TStreamId : notnull
    where TStreamPos : struct
    where TLogPos : struct
{
    /// <summary>
    /// Reads the position of the last event in the log, or <see langword="null"/> if the log is empty.
    /// </summary>
    ValueTask<TLogPos?> ReadEndPositionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads the events in the log that <paramref name="filter"/> selects, in log order, after
    /// <paramref name="fromExclusive"/>, or from the start if it is <see langword="null"/>, up to and including
    /// <paramref name="toInclusive"/>, or to the end of the log if it is <see langword="null"/>. Each event is valid
    /// only until the next one is read.
    /// </summary>
    IAsyncEnumerable<TSourceEvent> ReadAllAsync(
        TLogPos? fromExclusive,
        TLogPos? toInclusive,
        EventFilter filter,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads the position of the last event in a stream, or <see langword="null"/> if the stream is empty or does not
    /// exist.
    /// </summary>
    ValueTask<TStreamPos?> ReadStreamEndPositionAsync(TStreamId streamId, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the events in a stream that <paramref name="filter"/> selects, in stream order, after
    /// <paramref name="fromExclusive"/>, or from the start if it is <see langword="null"/>, up to and including
    /// <paramref name="toInclusive"/>. Each event is valid only until the next one is read.
    /// </summary>
    IAsyncEnumerable<TSourceEvent> ReadStreamAsync(
        TStreamId streamId,
        TStreamPos? fromExclusive,
        TStreamPos toInclusive,
        EventFilter filter,
        CancellationToken cancellationToken);

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
    /// Decides whether a failure of a read is transient. A transient failure of the shared read of the log is retried
    /// with backoff, and any other is not.
    /// </summary>
    bool IsTransientError(Exception exception);
}