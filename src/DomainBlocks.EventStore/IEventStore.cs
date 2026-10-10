namespace DomainBlocks.EventStore;

/// <summary>
/// Defines an event store, which appends events to streams and reads and subscribes to them, one stream at a time or
/// across the whole event log.
/// </summary>
/// <typeparam name="TEvent">The type of the events.</typeparam>
/// <typeparam name="TStreamId">The type of a stream identifier.</typeparam>
/// <typeparam name="TStreamPos">The type of a position within a stream.</typeparam>
/// <typeparam name="TLogPos">The type of a position in the event log.</typeparam>
public interface IEventStore<TEvent, TStreamId, TStreamPos, TLogPos> : IAsyncDisposable
    where TEvent : notnull
    where TStreamId : notnull
    where TStreamPos : notnull
    where TLogPos : notnull
{
    /// <summary>
    /// Creates the database objects the store needs, such as a schema or indexes, if they do not exist.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <remarks>
    /// Idempotent and safe to call concurrently, so it can run at every startup.
    /// </remarks>
    Task EnsureInitializedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends events to a stream.
    /// </summary>
    /// <param name="streamId">The stream to append to.</param>
    /// <param name="events">The events to append, in order.</param>
    /// <param name="expectedState">
    /// The state the stream must be in for the append to succeed, or <see langword="null"/> to accept any state.
    /// </param>
    /// <param name="commitId">
    /// An ID that makes the append idempotent, or <see langword="null"/> to generate one. An append whose ID is already
    /// committed succeeds without writing anything.
    /// </param>
    /// <param name="options">The append options, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
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
    /// <param name="direction">The direction to read in. The default is <see cref="ReadDirection.Forward"/>.</param>
    /// <param name="origin">
    /// Where to start reading. The default is the start of the event log for a forward read and the end for a backward
    /// read.
    /// </param>
    /// <param name="options">The read options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The events read.</returns>
    IAsyncEnumerable<ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>> ReadAll(
        ReadDirection direction = ReadDirection.Forward,
        ReadOrigin<TLogPos>? origin = null,
        ReadAllOptions? options = null);

    /// <summary>
    /// Reads events from a stream.
    /// </summary>
    /// <param name="streamId">The stream to read.</param>
    /// <param name="direction">The direction to read in. The default is <see cref="ReadDirection.Forward"/>.</param>
    /// <param name="origin">
    /// Where to start reading. The default is the start of the stream for a forward read and the end for a backward
    /// read.
    /// </param>
    /// <param name="options">The read options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The events read.</returns>
    IAsyncEnumerable<ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>> ReadStream(
        TStreamId streamId,
        ReadDirection direction = ReadDirection.Forward,
        ReadOrigin<TStreamPos>? origin = null,
        ReadStreamOptions? options = null);

    /// <summary>
    /// Subscribes to events appended to any stream.
    /// </summary>
    /// <param name="origin">
    /// Where to start the subscription. The default is <see cref="SubscriptionOrigin{TLogPos}.End"/>.
    /// </param>
    /// <param name="options">The subscription options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The subscription's messages.</returns>
    IAsyncEnumerable<SubscriptionMessage<TEvent, TStreamId, TStreamPos, TLogPos>> SubscribeToAll(
        SubscriptionOrigin<TLogPos>? origin = null,
        SubscriptionOptions? options = null);

    /// <summary>
    /// Subscribes to events appended to a stream.
    /// </summary>
    /// <param name="streamId">The stream to subscribe to.</param>
    /// <param name="origin">
    /// Where to start the subscription. The default is <see cref="SubscriptionOrigin{TStreamPos}.End"/>.
    /// </param>
    /// <param name="options">The subscription options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The subscription's messages.</returns>
    IAsyncEnumerable<SubscriptionMessage<TEvent, TStreamId, TStreamPos, TLogPos>> SubscribeToStream(
        TStreamId streamId,
        SubscriptionOrigin<TStreamPos>? origin = null,
        SubscriptionOptions? options = null);
}