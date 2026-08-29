using System.Diagnostics;
using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Abstractions.Codecs;
using DomainBlocks.MongoDB.Sequencing;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using AppendOptions = DomainBlocks.EventStore.Abstractions.AppendOptions;

namespace DomainBlocks.EventStore.MongoDB;

public static class MongoEventStore
{
    private const string SequenceIdFieldName = "event_log_seq";

    private static readonly StringFieldDefinition<BsonDocument, long> SequenceTargetField = new("_id");

    public static MongoEventStore<TEvent> Create<TEvent>(
        IMongoClient mongoClient,
        EventCodec<TEvent, BsonValue, BsonValue> codec,
        MongoEventStoreOptions? options = null,
        ILogger? logger = null)
        where TEvent : notnull
    {
        options ??= new MongoEventStoreOptions();

        var db = mongoClient.GetDatabase(options.DatabaseName);

        var sequenceBinding = new MongoSequenceBinding<BsonDocument>(
            new CollectionNamespace(db.DatabaseNamespace, options.SequencesCollectionName),
            SequenceIdFieldName,
            new CollectionNamespace(db.DatabaseNamespace, options.EventLogCollectionName),
            SequenceTargetField);

        var eventLog = db.GetCollection<BsonDocument>(options.EventLogCollectionName);

        var sequencedAppender = new MongoSequencedAppender<BsonDocument, AppendToStreamContext>(
            mongoClient,
            sequenceBinding,
            new AppendToStreamPolicy(eventLog),
            new MongoSequencedAppenderOptions
            {
                QueueCapacity = options.AppendQueueCapacity,
                BatchSize = options.AppendBatchSize
            },
            logger);

        return new MongoEventStore<TEvent>(sequencedAppender, eventLog, codec);
    }
}

public sealed class MongoEventStore<TEvent>(
    IMongoSequencedAppender<BsonDocument, AppendToStreamContext> sequencedAppender,
    IMongoCollection<BsonDocument> eventLog,
    EventCodec<TEvent, BsonValue, BsonValue> eventCodec) :
    IMongoEventStore<TEvent>
    where TEvent : notnull
{
    private readonly IEventEncoder<TEvent, BsonValue, BsonValue> _encoder = eventCodec.Encoder;
    private readonly IEventDecoder<TEvent, BsonValue, BsonValue> _decoder = eventCodec.Decoder;

    public async Task AppendAsync(
        string streamId,
        IEnumerable<AppendableEvent<TEvent>> events,
        ExpectedStreamState<StreamPosition>? expectedState = null,
        Guid? commitId = null,
        AppendOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        expectedState ??= ExpectedStreamState<StreamPosition>.Any;
        commitId ??= Guid.NewGuid();
        options ??= new AppendOptions();

        var bsonStreamId = new BsonString(streamId);
        var bsonCommitId = new BsonBinaryData(commitId.Value, GuidRepresentation.Standard);

        var eventDocuments = _encoder
            .Encode(events)
            .Select((x, i) => new BsonDocument
            {
                { EventLogEntry.FieldNames.StreamId, bsonStreamId },
                { EventLogEntry.FieldNames.CommitId, bsonCommitId },
                { EventLogEntry.FieldNames.CommitIndex, i },
                { EventLogEntry.FieldNames.EventName, x.EventName },
                { EventLogEntry.FieldNames.EventData, x.EventData },
                { EventLogEntry.FieldNames.Metadata, x.Metadata ?? BsonNull.Value }
            });

        var context = new AppendToStreamContext(commitId.Value, streamId, expectedState.Value);
        var appendOptions = new DomainBlocks.MongoDB.Sequencing.AppendOptions { Timeout = options.Timeout };

        await sequencedAppender
            .AppendAsync(eventDocuments, context, appendOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    public IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadAll(
        ReadDirection direction = ReadDirection.Forward,
        ReadOrigin<LogPosition>? origin = null,
        ReadAllOptions? options = null)
    {
        options ??= ReadAllOptions.Default;
        return ReadCoreAsync(direction, origin, "_id", options.MaxCount);
    }

    public IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadStream(
        string streamId,
        ReadDirection direction = ReadDirection.Forward,
        ReadOrigin<StreamPosition>? origin = null,
        ReadStreamOptions? options = null)
    {
        options ??= ReadStreamOptions.Default;

        var isEmptyEnumeration = direction == ReadDirection.Forward && origin is ReadOrigin<StreamPosition>.End ||
                                 direction == ReadDirection.Backward && origin is ReadOrigin<StreamPosition>.Start;

        return isEmptyEnumeration
            ? ReadEmptyAsync()
            : ReadCoreAsync(
                direction,
                origin,
                EventLogEntry.FieldNames.StreamVersion,
                options.MaxCount,
                streamId,
                emptyAction: () =>
                {
                    if (options.StreamNotFoundBehavior == StreamNotFoundBehavior.Throw)
                        throw new StreamNotFoundException(streamId);
                });

        async IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadEmptyAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (options.StreamNotFoundBehavior == StreamNotFoundBehavior.Throw &&
                !await StreamExistsAsync(streamId, cancellationToken).ConfigureAwait(false))
            {
                throw new StreamNotFoundException(streamId);
            }

            yield break;
        }
    }

    public IAsyncEnumerable<SubscriptionMessage> SubscribeToAll(
        ReadOrigin<LogPosition>? origin = null,
        SubscriptionOptions? options = null)
    {
        throw new NotImplementedException();
    }

    public IAsyncEnumerable<SubscriptionMessage> SubscribeToStream(
        string streamId,
        ReadOrigin<StreamPosition>? origin = null,
        SubscriptionOptions? options = null)
    {
        throw new NotImplementedException();
    }

    public ValueTask DisposeAsync() => sequencedAppender.DisposeAsync();

    private static ReadQuery GetReadQuery<TPos>(
        ReadDirection direction,
        ReadOrigin<TPos>? origin,
        string positionFieldName)
        where TPos : struct,
        IPosition<TPos>
    {
        origin ??= direction == ReadDirection.Forward ? ReadOrigin.Start<TPos>() : ReadOrigin.End<TPos>();

        return origin switch
        {
            ReadOrigin<TPos>.Start when direction == ReadDirection.Forward => new ReadQuery(
                Builders<BsonDocument>.Filter.Empty,
                Builders<BsonDocument>.Sort.Ascending(positionFieldName)),

            ReadOrigin<TPos>.End when direction == ReadDirection.Backward => new ReadQuery(
                Builders<BsonDocument>.Filter.Empty,
                Builders<BsonDocument>.Sort.Descending(positionFieldName)),

            ReadOrigin<TPos>.Position p when direction == ReadDirection.Forward => new ReadQuery(
                Builders<BsonDocument>.Filter.Gte(positionFieldName, p.Value.Value),
                Builders<BsonDocument>.Sort.Ascending(positionFieldName)),

            ReadOrigin<TPos>.Position p when direction == ReadDirection.Backward => new ReadQuery(
                Builders<BsonDocument>.Filter.Lte(positionFieldName, p.Value.Value),
                Builders<BsonDocument>.Sort.Descending(positionFieldName)),

            _ => throw new UnreachableException($"Unknown ReadOrigin type '{origin.GetType().Name}'.")
        };
    }

    private async IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadCoreAsync<TPos>(
        ReadDirection direction,
        ReadOrigin<TPos>? origin,
        string positionFieldName,
        int? maxCount,
        string? streamId = null,
        Action? emptyAction = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where TPos : struct,
        IPosition<TPos>
    {
        var query = GetReadQuery(direction, origin, positionFieldName);

        var filter = streamId is null
            ? query.Filter
            : query.Filter & Builders<BsonDocument>.Filter.Eq(EventLogEntry.FieldNames.StreamId, streamId);

        using var cursor = await eventLog
            .Find(filter)
            .Sort(query.Sort)
            .Limit(maxCount)
            .ToCursorAsync(cancellationToken)
            .ConfigureAwait(false);

        var isEmpty = true;

        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var doc in cursor.Current)
            {
                isEmpty = false;

                var logPosition = LogPosition.FromInt64(doc["_id"].AsInt64);
                var streamIdFromEvent = doc[EventLogEntry.FieldNames.StreamId].AsString;
                var streamPosition = StreamPosition.FromInt64(doc[EventLogEntry.FieldNames.StreamVersion].AsInt64);
                var eventName = doc[EventLogEntry.FieldNames.EventName].AsString;
                var eventData = doc[EventLogEntry.FieldNames.EventData];
                var metadata = doc[EventLogEntry.FieldNames.Metadata];
                var writtenAtUtc = doc[EventLogEntry.FieldNames.WrittenAtUtc].AsBsonDateTime.ToUniversalTime();

                var (@event, decodedMetadata) = _decoder.Decode(eventName, eventData, metadata);

                var context = ReadEventContext.Create(
                    streamIdFromEvent,
                    decodedMetadata,
                    writtenAtUtc,
                    streamPosition,
                    logPosition);

                yield return ReadEvent.Create(@event, context);
            }
        }

        if (isEmpty)
            emptyAction?.Invoke();
    }

    private async Task<bool> StreamExistsAsync(string streamId, CancellationToken cancellationToken)
    {
        var filter = Builders<BsonDocument>.Filter.Eq(EventLogEntry.FieldNames.StreamId, streamId);
        return await eventLog.Find(filter).AnyAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed class ReadQuery(FilterDefinition<BsonDocument> filter, SortDefinition<BsonDocument> sort)
    {
        public FilterDefinition<BsonDocument> Filter { get; } = filter;
        public SortDefinition<BsonDocument> Sort { get; } = sort;
    }
}