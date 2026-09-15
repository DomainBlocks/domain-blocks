using System.Diagnostics;
using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.MongoDB.ChangeStreams;
using DomainBlocks.MongoDB.Sequencing;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

public static class MongoEventStore
{
    private const string SequenceIdFieldName = "event_log_seq";

    private static readonly StringFieldDefinition<BsonDocument, long> SequenceTargetField =
        new(EventLogEntry.FieldNames.Position);

    public static MongoEventStore<TEvent> Create<TEvent>(
        IMongoClient mongoClient,
        IEventCodec<TEvent, BsonValue, BsonValue> eventCodec,
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

        var sequencedAppender = new MongoSequencedAppender<BsonDocument, AppendContext>(
            mongoClient,
            sequenceBinding,
            new AppenderPolicy(eventLog),
            new MongoSequencedAppenderOptions
            {
                QueueCapacity = options.AppendQueueCapacity,
                MaxBatchSize = options.AppendBatchSize
            },
            logger);

        return new MongoEventStore<TEvent>(sequencedAppender, eventLog, eventCodec, logger);
    }
}

public sealed class MongoEventStore<TEvent> : IMongoEventStore<TEvent> where TEvent : notnull
{
    private readonly IMongoSequencedAppender<BsonDocument, AppendContext> _sequencedAppender;
    private readonly IMongoCollection<BsonDocument> _eventLog;
    private readonly IEventCodec<TEvent, BsonValue, BsonValue> _eventCodec;
    private readonly ILogger? _logger;
    private readonly RefCountedChangeStreamSubject<ChangeStreamDocument<BsonDocument>> _allEventsSubject;

    public MongoEventStore(
        IMongoSequencedAppender<BsonDocument, AppendContext> sequencedAppender,
        IMongoCollection<BsonDocument> eventLog,
        IEventCodec<TEvent, BsonValue, BsonValue> eventCodec,
        ILogger? logger = null)
    {
        _sequencedAppender = sequencedAppender;
        _eventCodec = eventCodec;
        _logger = logger;

        _eventLog = eventLog
            .WithReadConcern(ReadConcern.Majority)
            .WithReadPreference(ReadPreference.Primary);

        _allEventsSubject = CreateAllEventsSubject(_eventLog, logger);
    }

    public async Task AppendAsync(
        string streamId,
        IEnumerable<AppendableEvent<TEvent>> events,
        ExpectedStreamState<StreamPosition>? expectedState = null,
        Guid? commitId = null,
        AppendOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        expectedState ??= ExpectedStreamState.Any<StreamPosition>();
        commitId ??= Guid.NewGuid();
        options ??= new AppendOptions();

        var bsonStreamId = new BsonString(streamId);
        var bsonCommitId = new BsonBinaryData(commitId.Value, GuidRepresentation.Standard);

        var eventDocuments = _eventCodec
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

        var context = new AppendContext(commitId.Value, streamId, expectedState.Value);
        var appendOptions = new DomainBlocks.MongoDB.Sequencing.AppendOptions { Timeout = options.Timeout };

        await _sequencedAppender
            .AppendAsync(eventDocuments, context, appendOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    public IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadAll(
        ReadDirection direction = ReadDirection.Forward,
        ReadOrigin<LogPosition>? origin = null,
        ReadAllOptions? options = null)
    {
        return Impl();

        async IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> Impl(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            origin ??= direction == ReadDirection.Forward
                ? ReadOrigin.Start<LogPosition>()
                : ReadOrigin.End<LogPosition>();

            options ??= ReadAllOptions.Default;

            if (direction.ProducesEmptyReadFrom(origin))
                yield break;

            var query = GetReadQuery(direction, origin, EventLogEntry.FieldNames.Position);

            using var cursor = await _eventLog
                .Find(query.Filter)
                .Sort(query.Sort)
                .Limit(options.MaxCount)
                .ProjectMetadata(options.IncludeMetadata)
                .ToCursorAsync(cancellationToken)
                .ConfigureAwait(false);

            while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
            {
                foreach (var doc in cursor.Current)
                    yield return _eventCodec.Decode(doc);
            }
        }
    }

    public IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadStream(
        string streamId,
        ReadDirection direction = ReadDirection.Forward,
        ReadOrigin<StreamPosition>? origin = null,
        ReadStreamOptions? options = null)
    {
        return Impl();

        async IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> Impl(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            origin ??= direction == ReadDirection.Forward
                ? ReadOrigin.Start<StreamPosition>()
                : ReadOrigin.End<StreamPosition>();

            options ??= ReadStreamOptions.Default;

            if (direction.ProducesEmptyReadFrom(origin))
            {
                if (options.StreamNotFoundBehavior == StreamNotFoundBehavior.Throw &&
                    !await StreamExistsAsync(streamId, cancellationToken).ConfigureAwait(false))
                {
                    throw new StreamNotFoundException(streamId);
                }

                yield break;
            }

            var query = GetReadQuery(direction, origin, EventLogEntry.FieldNames.StreamPosition);
            var filter = query.Filter & Builders<BsonDocument>.Filter.Eq(EventLogEntry.FieldNames.StreamId, streamId);

            using var cursor = await _eventLog
                .Find(filter)
                .Sort(query.Sort)
                .Limit(options.MaxCount)
                .ProjectMetadata(options.IncludeMetadata)
                .ToCursorAsync(cancellationToken)
                .ConfigureAwait(false);

            var isEmpty = true;

            while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
            {
                foreach (var doc in cursor.Current)
                {
                    isEmpty = false;
                    yield return _eventCodec.Decode(doc);
                }
            }

            // An empty range of an existing stream is not a missing stream.
            if (isEmpty &&
                options.StreamNotFoundBehavior == StreamNotFoundBehavior.Throw &&
                !await StreamExistsAsync(streamId, cancellationToken).ConfigureAwait(false))
            {
                throw new StreamNotFoundException(streamId);
            }
        }
    }

    public IAsyncEnumerable<SubscriptionMessage> SubscribeToAll(
        SubscriptionOrigin<LogPosition>? origin = null,
        SubscriptionOptions? options = null)
    {
        return new SubscriptionAsyncEnumerable<TEvent, LogPosition>(
            _eventLog,
            _allEventsSubject,
            _eventCodec,
            Builders<BsonDocument>.Filter.Empty,
            static _ => true,
            EventLogEntry.FieldNames.Position,
            static ctx => ctx.LogPosition,
            origin,
            options,
            _logger);
    }

    public IAsyncEnumerable<SubscriptionMessage> SubscribeToStream(
        string streamId,
        SubscriptionOrigin<StreamPosition>? origin = null,
        SubscriptionOptions? options = null)
    {
        return new SubscriptionAsyncEnumerable<TEvent, StreamPosition>(
            _eventLog,
            _allEventsSubject,
            _eventCodec,
            Builders<BsonDocument>.Filter.Eq(EventLogEntry.FieldNames.StreamId, streamId),
            ctx => ctx.StreamId == streamId,
            EventLogEntry.FieldNames.StreamPosition,
            static ctx => ctx.StreamPosition,
            origin,
            options,
            _logger);
    }

    public ValueTask DisposeAsync() => _sequencedAppender.DisposeAsync();

    private static RefCountedChangeStreamSubject<ChangeStreamDocument<BsonDocument>> CreateAllEventsSubject(
        IMongoCollection<BsonDocument> eventLog,
        ILogger? logger)
    {
        var insertsOnly = Builders<ChangeStreamDocument<BsonDocument>>.Filter.Eq(
            x => x.OperationType,
            ChangeStreamOperationType.Insert);

        return RefCountedChangeStreamSubject.Create(
            eventLog.Database.Client,
            eventLog.WatchAsync,
            new EmptyPipelineDefinition<ChangeStreamDocument<BsonDocument>>().Match(insertsOnly),
            doc => doc.ResumeToken,
            logger: logger);
    }

    private static ReadQuery GetReadQuery<TPos>(
        ReadDirection direction,
        ReadOrigin<TPos> origin,
        string positionFieldName)
        where TPos : struct,
        IPosition<TPos>
    {
        return origin switch
        {
            ReadOrigin<TPos>.Start when direction == ReadDirection.Forward => new ReadQuery(
                Builders<BsonDocument>.Filter.Empty,
                Builders<BsonDocument>.Sort.Ascending(positionFieldName)),

            ReadOrigin<TPos>.End when direction == ReadDirection.Backward => new ReadQuery(
                Builders<BsonDocument>.Filter.Empty,
                Builders<BsonDocument>.Sort.Descending(positionFieldName)),

            ReadOrigin<TPos>.At at when direction == ReadDirection.Forward => new ReadQuery(
                Builders<BsonDocument>.Filter.Gte(positionFieldName, at.Position.Value),
                Builders<BsonDocument>.Sort.Ascending(positionFieldName)),

            ReadOrigin<TPos>.At at when direction == ReadDirection.Backward => new ReadQuery(
                Builders<BsonDocument>.Filter.Lte(positionFieldName, at.Position.Value),
                Builders<BsonDocument>.Sort.Descending(positionFieldName)),

            _ => throw new UnreachableException($"Unexpected ReadOrigin type '{origin.GetType().Name}'.")
        };
    }

    private async Task<bool> StreamExistsAsync(string streamId, CancellationToken cancellationToken)
    {
        var filter = Builders<BsonDocument>.Filter.Eq(EventLogEntry.FieldNames.StreamId, streamId);
        return await _eventLog.Find(filter).AnyAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed class ReadQuery(FilterDefinition<BsonDocument> filter, SortDefinition<BsonDocument> sort)
    {
        public FilterDefinition<BsonDocument> Filter { get; } = filter;
        public SortDefinition<BsonDocument> Sort { get; } = sort;
    }
}