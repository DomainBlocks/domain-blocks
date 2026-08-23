using System.Diagnostics;
using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Abstractions.Codecs;
using DomainBlocks.MongoDB.Sequencing;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

public static class MongoEventStoreClient
{
    private const string SequenceIdFieldName = "event_log_seq";

    private static readonly StringFieldDefinition<BsonDocument, long> SequenceTargetField = new("_id");

    public static MongoEventStoreClient<TEvent> Create<TEvent>(
        IMongoClient mongoClient,
        EventCodec<TEvent, BsonValue, BsonValue> codec,
        MongoEventStoreClientOptions? options = null,
        ILogger? logger = null)
        where TEvent : notnull
    {
        options ??= new MongoEventStoreClientOptions();

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

        return new MongoEventStoreClient<TEvent>(sequencedAppender, eventLog, codec);
    }
}

public sealed class MongoEventStoreClient<TEvent>(
    IMongoSequencedAppender<BsonDocument, AppendToStreamContext> sequencedAppender,
    IMongoCollection<BsonDocument> eventLog,
    EventCodec<TEvent, BsonValue, BsonValue> eventCodec) :
    IMongoEventStoreClient<TEvent>
    where TEvent : notnull
{
    private readonly IEventEncoder<TEvent, BsonValue, BsonValue> _encoder = eventCodec.Encoder;
    private readonly IEventDecoder<TEvent, BsonValue, BsonValue> _decoder = eventCodec.Decoder;

    public async Task AppendToStreamAsync(
        string streamId,
        ExpectedStreamState<StreamPosition> expectedState,
        IEnumerable<AppendEvent<TEvent>> events,
        Guid? commitId = null,
        AppendToStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        commitId ??= Guid.NewGuid();
        options ??= new AppendToStreamOptions();

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

        var context = new AppendToStreamContext(commitId.Value, streamId, expectedState);
        var appendOptions = new AppendOptions { Timeout = options.Timeout };

        await sequencedAppender
            .AppendAsync(eventDocuments, context, appendOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    public IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadAll(
        ReadDefinition<LogPosition> definition,
        ReadAllOptions? options = null)
    {
        options ??= ReadAllOptions.Default;
        return ReadCoreAsync(definition, "_id", options.MaxCount);
    }

    public IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadStream(
        string streamId,
        ReadDefinition<StreamPosition> definition,
        ReadStreamOptions? options = null)
    {
        options ??= ReadStreamOptions.Default;
        return ReadCoreAsync(definition, EventLogEntry.FieldNames.StreamVersion, options.MaxCount, streamId);
    }

    public IAsyncEnumerable<SubscriptionMessage> SubscribeToAll(
        SubscriptionDefinition<LogPosition> definition,
        SubscriptionOptions? options = null)
    {
        throw new NotImplementedException();
    }

    public IAsyncEnumerable<SubscriptionMessage> SubscribeToStream(
        string streamId,
        SubscriptionDefinition<StreamPosition> definition,
        SubscriptionOptions? options = null)
    {
        throw new NotImplementedException();
    }

    public ValueTask DisposeAsync() => sequencedAppender.DisposeAsync();

    private static ReadQuery GetReadQuery<TPos>(ReadDefinition<TPos> definition, string positionFieldName)
        where TPos : struct,
        IPosition<TPos>
    {
        return definition switch
        {
            ReadDefinition<TPos>.ForwardFromStart => new ReadQuery(
                Builders<BsonDocument>.Filter.Empty,
                Builders<BsonDocument>.Sort.Ascending(positionFieldName)),

            ReadDefinition<TPos>.BackwardFromEnd => new ReadQuery(
                Builders<BsonDocument>.Filter.Empty,
                Builders<BsonDocument>.Sort.Descending(positionFieldName)),

            ReadDefinition<TPos>.ForwardFrom d => new ReadQuery(
                Builders<BsonDocument>.Filter.Gte(positionFieldName, d.Position.Value),
                Builders<BsonDocument>.Sort.Ascending(positionFieldName)),

            ReadDefinition<TPos>.BackwardFrom d => new ReadQuery(
                Builders<BsonDocument>.Filter.Lte(positionFieldName, d.Position.Value),
                Builders<BsonDocument>.Sort.Descending(positionFieldName)),

            _ => throw new UnreachableException($"Unknown ReadDefinition type '{definition.GetType().Name}'.")
        };
    }

    private async IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadCoreAsync<TPos>(
        ReadDefinition<TPos> definition,
        string positionFieldName,
        int? maxCount,
        string? streamId = null,
        Action? emptyAction = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where TPos : struct,
        IPosition<TPos>
    {
        var query = GetReadQuery(definition, positionFieldName);

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

                yield return ReadEvent.Create(
                    @event,
                    streamIdFromEvent,
                    decodedMetadata,
                    writtenAtUtc,
                    streamPosition,
                    logPosition);
            }
        }

        if (isEmpty)
            emptyAction?.Invoke();
    }

    private sealed class ReadQuery(FilterDefinition<BsonDocument> filter, SortDefinition<BsonDocument> sort)
    {
        public FilterDefinition<BsonDocument> Filter { get; } = filter;
        public SortDefinition<BsonDocument> Sort { get; } = sort;
    }
}