using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Internal.Feeds;
using DomainBlocks.EventStore.Internal.Subscriptions;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

/// <summary>
/// Implements a catch-up subscription whose replay reads in a session that waits for the change stream's high-water
/// mark.
/// </summary>
internal sealed class MongoSubscription<TEvent, TPos>(
    EventLogReader<TEvent> reader,
    IRefCountedFeed<EventLogDocument<TEvent>, BsonTimestamp> feed,
    MongoSubscription<TEvent, TPos>.CatchUpReader catchUpReader,
    MongoSubscription<TEvent, TPos>.EndPositionReader endPositionReader,
    EventFilter liveFilter,
    Func<ReadEventContext<string, StreamPosition, LogPosition>, TPos> positionSelector,
    SubscriptionOrigin<TPos>? origin,
    SubscriptionOptions? options,
    ILogger? logger) :
    SubscriptionAsyncEnumerable<TEvent, EventLogDocument<TEvent>, BsonTimestamp, TPos>(
        feed,
        liveFilter,
        positionSelector,
        origin,
        options,
        MongoTransientErrors.IsTransient,
        logger)
    where TEvent : notnull
    where TPos : struct, IPosition<TPos>
{
    private readonly EventLogReader<TEvent> _reader = reader;
    private readonly CatchUpReader _catchUpReader = catchUpReader;
    private readonly EndPositionReader _endPositionReader = endPositionReader;

    protected override ValueTask<TPos?> ReadEndAsync(CancellationToken cancellationToken)
    {
        return new ValueTask<TPos?>(_endPositionReader(_reader, null, cancellationToken));
    }

    protected override async ValueTask<ICatchUp<TEvent, TPos>> CreateCatchUpAsync(
        BsonTimestamp highWaterMark,
        CancellationToken cancellationToken)
    {
        var session = await _reader.StartCatchUpSessionAsync(highWaterMark, cancellationToken).ConfigureAwait(false);

        return new SessionCatchUp(this, session);
    }

    /// <summary>
    /// Reads the followed sequence from one position to another.
    /// </summary>
    public delegate IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> CatchUpReader(
        EventLogReader<TEvent> reader,
        IClientSessionHandle session,
        long startExclusive,
        long endInclusive,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads the end position of the followed sequence, or <see langword="null"/> if it is empty.
    /// </summary>
    public delegate Task<TPos?> EndPositionReader(
        EventLogReader<TEvent> reader,
        IClientSessionHandle? session,
        CancellationToken cancellationToken);

    /// <summary>
    /// Represents the reads of one catch-up, in a session that waits for the high-water mark, so the end position and
    /// the replay include every event that the observer won't receive from the stream.
    /// </summary>
    private sealed class SessionCatchUp(MongoSubscription<TEvent, TPos> subscription, IClientSessionHandle session) :
        ICatchUp<TEvent, TPos>
    {
        public ValueTask<TPos?> ReadEndAsync(CancellationToken cancellationToken)
        {
            return new ValueTask<TPos?>(
                subscription._endPositionReader(subscription._reader, session, cancellationToken));
        }

        public IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadAsync(
            long startExclusive,
            long endInclusive,
            CancellationToken cancellationToken)
        {
            return subscription._catchUpReader(
                subscription._reader,
                session,
                startExclusive,
                endInclusive,
                cancellationToken);
        }

        public void Dispose() => session.Dispose();
    }
}