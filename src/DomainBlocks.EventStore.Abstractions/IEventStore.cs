namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// Defines operations for appending to and reading from an event store.
/// </summary>
/// <typeparam name="TEvent">The type of events stored by the event store.</typeparam>
/// <typeparam name="TStreamId">The type used to identify event streams.</typeparam>
/// <typeparam name="TStreamPos">The type used to represent positions within a stream.</typeparam>
/// <typeparam name="TLogPos">The type used to represent positions in the global event log.</typeparam>
public interface IEventStore<TEvent, TStreamId, TStreamPos, TLogPos>
    where TEvent : notnull
    where TStreamId : notnull
    where TStreamPos : notnull
    where TLogPos : notnull
{
    /// <summary>
    /// Appends events to a stream.
    /// </summary>
    /// <param name="streamId">The identifier of the stream to append to.</param>
    /// <param name="events">The events to append, in their append order.</param>
    /// <param name="expectedState">
    /// The expected state of the stream, or <see langword="null"/> to impose no constraint (default).
    /// </param>
    /// <param name="commitId">An optional identifier used to make the append operation idempotent.</param>
    /// <param name="options">Options that configure the append operation.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous append operation.</returns>
    Task AppendAsync(
        TStreamId streamId,
        IEnumerable<AppendableEvent<TEvent>> events,
        ExpectedStreamState<TStreamPos>? expectedState = null,
        Guid? commitId = null,
        AppendOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads events across all streams.
    /// </summary>
    /// <param name="direction">
    /// The direction in which to read events. The default is <see cref="ReadDirection.Forward"/>.
    /// </param>
    /// <param name="origin">
    /// The position from which to start reading. The default is <see cref="ReadOrigin{TLogPos}.Start"/>.
    /// </param>
    /// <param name="options">Options that configure the read operation.</param>
    /// <returns>An asynchronous sequence of events from all streams.</returns>
    IAsyncEnumerable<ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>> ReadAll(
        ReadDirection direction = ReadDirection.Forward,
        ReadOrigin<TLogPos>? origin = null,
        ReadAllOptions? options = null);

    /// <summary>
    /// Reads events from a stream.
    /// </summary>
    /// <param name="streamId">The identifier of the stream to read.</param>
    /// <param name="direction">
    /// The direction in which to read events. The default is <see cref="ReadDirection.Forward"/>.
    /// </param>
    /// <param name="origin">
    /// The position from which to start reading. The default is <see cref="ReadOrigin{TStreamPos}.Start"/>.
    /// </param>
    /// <param name="options">Options that configure the read operation.</param>
    /// <returns>An asynchronous sequence of events from the specified stream.</returns>
    IAsyncEnumerable<ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>> ReadStream(
        TStreamId streamId,
        ReadDirection direction = ReadDirection.Forward,
        ReadOrigin<TStreamPos>? origin = null,
        ReadStreamOptions? options = null);

    /// <summary>
    /// Subscribes to events appended to any stream.
    /// </summary>
    /// <param name="origin">
    /// The position from which to start receiving events. The default is <see cref="ReadOrigin{TLogPos}.Start"/>.
    /// </param>
    /// <param name="options">Options that configure the subscription.</param>
    /// <returns>An asynchronous sequence of subscription messages.</returns>
    IAsyncEnumerable<SubscriptionMessage> SubscribeToAll(
        ReadOrigin<TLogPos>? origin = null,
        SubscriptionOptions? options = null);

    /// <summary>
    /// Subscribes to events appended to a stream.
    /// </summary>
    /// <param name="streamId">The identifier of the stream to subscribe to.</param>
    /// <param name="origin">
    /// The position from which to start receiving events. The default is <see cref="ReadOrigin{TStreamPos}.Start"/>.
    /// </param>
    /// <param name="options">Options that configure the subscription.</param>
    /// <returns>An asynchronous sequence of subscription messages.</returns>
    IAsyncEnumerable<SubscriptionMessage> SubscribeToStream(
        TStreamId streamId,
        ReadOrigin<TStreamPos>? origin = null,
        SubscriptionOptions? options = null);
}