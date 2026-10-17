using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Filtering.Nodes;
using DomainBlocks.EventStore.Subscriptions;
using MongoDB.Bson;
using MongoDB.Driver;
using static DomainBlocks.EventStore.MongoDB.EventLogEntry;

namespace DomainBlocks.EventStore.MongoDB;

/// <summary>
/// Reads the event log for subscriptions, and listens for the documents inserted into it.
/// </summary>
/// <remarks>
/// <para>
/// Reads are majority reads on the primary, in a causally consistent session of the source's own, advanced to the
/// store's <see cref="OperationTimeFence"/> before each read, so a read sees every event that another source has read.
/// Documents are read into one document that the source reuses, which is valid only until the next is read.
/// </para>
/// <para>
/// Listening uses a change stream of inserts that carries only each document's key, which is its log position.
/// </para>
/// </remarks>
internal sealed class MongoSubscriptionSource<TEvent>(
    IMongoCollection<BsonDocument> eventLog,
    OperationTimeFence fence,
    IEventDecoder<TEvent, BsonValue, BsonValue> decoder) :
    IEventSubscriptionSource<EventLogDocument<TEvent>, string, StreamPosition, LogPosition>
    where TEvent : notnull
{
    private static readonly PipelineDefinition<ChangeStreamDocument<BsonDocument>, BsonDocument> InsertedPositions =
        new EmptyPipelineDefinition<ChangeStreamDocument<BsonDocument>>()
            .Match(x => x.OperationType == ChangeStreamOperationType.Insert)
            .Project(Builders<ChangeStreamDocument<BsonDocument>>.Projection.Include("documentKey"));

    private readonly EventLogDocument<TEvent> _document = new(decoder);
    private IClientSessionHandle? _session;

    public async ValueTask<LogPosition?> ReadEndPositionAsync(CancellationToken cancellationToken)
    {
        var position = await ReadEndAsync(Builders<BsonDocument>.Filter.Empty, FieldNames.Position, cancellationToken)
            .ConfigureAwait(false);

        return position is { } p ? LogPosition.FromInt64(p) : null;
    }

    public IAsyncEnumerable<EventLogDocument<TEvent>> ReadAllAsync(
        LogPosition? fromExclusive,
        LogPosition? toInclusive,
        EventFilter filter,
        CancellationToken cancellationToken)
    {
        var range = Builders<BsonDocument>.Filter.Gt(FieldNames.Position, ToKey(fromExclusive));

        if (toInclusive is { } to)
            range &= Builders<BsonDocument>.Filter.Lte(FieldNames.Position, checked((long)to.Value));

        return ReadAsync(WithFilter(range, filter), FieldNames.Position, cancellationToken);
    }

    public async ValueTask<StreamPosition?> ReadStreamEndPositionAsync(
        string streamId,
        CancellationToken cancellationToken)
    {
        var position = await ReadEndAsync(
                Builders<BsonDocument>.Filter.Eq(FieldNames.StreamId, streamId),
                FieldNames.StreamPosition,
                cancellationToken)
            .ConfigureAwait(false);

        return position is { } p ? StreamPosition.FromInt64(p) : null;
    }

    public IAsyncEnumerable<EventLogDocument<TEvent>> ReadStreamAsync(
        string streamId,
        StreamPosition? fromExclusive,
        StreamPosition toInclusive,
        EventFilter filter,
        CancellationToken cancellationToken)
    {
        var range = Builders<BsonDocument>.Filter.Eq(FieldNames.StreamId, streamId) &
                    Builders<BsonDocument>.Filter.Gt(FieldNames.StreamPosition, ToKey(fromExclusive)) &
                    Builders<BsonDocument>.Filter.Lte(FieldNames.StreamPosition, checked((long)toInclusive.Value));

        return ReadAsync(WithFilter(range, filter), FieldNames.StreamPosition, cancellationToken);
    }

    public async IAsyncEnumerable<LogPosition> ListenForAppendsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // The change stream runs alongside the reads, so it does not use the source's session.
        using var cursor = await eventLog
            .WatchAsync(InsertedPositions, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            // One wake per batch is enough, so only the batch's last position matters.
            if (cursor.Current.LastOrDefault() is { } change)
                yield return LogPosition.FromInt64(change["documentKey"][FieldNames.Position].ToInt64());
        }
    }

    public bool IsTransientError(Exception exception) => MongoTransientErrors.IsTransient(exception);

    public ValueTask DisposeAsync()
    {
        _session?.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<long?> ReadEndAsync(
        FilterDefinition<BsonDocument> filter,
        string positionFieldName,
        CancellationToken cancellationToken)
    {
        var session = await StartReadAsync(cancellationToken).ConfigureAwait(false);

        var last = await eventLog
            .Find(session, filter)
            .Sort(Builders<BsonDocument>.Sort.Descending(positionFieldName))
            .Limit(1)
            .Project(Builders<BsonDocument>.Projection.Include(positionFieldName))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        fence.Advance(session.OperationTime);

        return last?[positionFieldName].ToInt64();
    }

    private async IAsyncEnumerable<EventLogDocument<TEvent>> ReadAsync(
        FilterDefinition<BsonDocument> filter,
        string positionFieldName,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var session = await StartReadAsync(cancellationToken).ConfigureAwait(false);

        using var cursor = await eventLog
            .Find(session, filter)
            .Sort(Builders<BsonDocument>.Sort.Ascending(positionFieldName))
            .ToCursorAsync(cancellationToken)
            .ConfigureAwait(false);

        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            // Advanced before the batch is handed on, so a later read by any source sees at least what this one saw.
            fence.Advance(session.OperationTime);

            foreach (var document in cursor.Current)
            {
                _document.Set(document);
                yield return _document;
            }
        }
    }

    // A read waits on the server until its node has a majority-committed view at least as recent as the fence.
    private async Task<IClientSessionHandle> StartReadAsync(CancellationToken cancellationToken)
    {
        _session ??= await eventLog.Database.Client
            .StartSessionAsync(new ClientSessionOptions { CausalConsistency = true }, cancellationToken)
            .ConfigureAwait(false);

        if (fence.Time is { } time)
            _session.AdvanceOperationTime(time);

        return _session;
    }

    private static FilterDefinition<BsonDocument> WithFilter(FilterDefinition<BsonDocument> range, EventFilter filter)
    {
        return filter is AllEventsFilter ? range : range & MongoFilterTranslator.Translate(filter);
    }

    private static long ToKey<TPos>(TPos? position) where TPos : struct, IPosition<TPos>
    {
        return position is { } p ? checked((long)p.Value) : -1;
    }
}