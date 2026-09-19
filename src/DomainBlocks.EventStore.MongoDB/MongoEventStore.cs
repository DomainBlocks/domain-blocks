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

    internal static MongoEventStore<TEvent> Create<TEvent>(
        IMongoClient mongoClient,
        IEventCodec<TEvent, BsonValue, BsonValue> eventCodec,
        MongoEventStoreOptions options,
        ILogger? logger,
        IDisposable? ownedClient)
        where TEvent : notnull
    {
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

        return new MongoEventStore<TEvent>(
            mongoClient,
            ownedClient,
            options,
            sequencedAppender,
            eventLog,
            eventCodec,
            logger);
    }
}

/// <summary>
/// A MongoDB event store: sequenced appender, event log collection and change-stream subject over a client. Created
/// by <see cref="MongoEventStore.Create{TEvent}"/> over a client the caller owns, or by
/// <see cref="MongoEventStoreBuilder{TEvent}"/>, which may also create a client for the store to own. Disposing the
/// store releases its append queue, and the client only when the store owns it.
/// </summary>
public sealed class MongoEventStore<TEvent> : IEventStore<TEvent, string, StreamPosition, LogPosition>
    where TEvent : notnull
{
    private readonly IMongoClient _client;
    private readonly IDisposable? _ownedClient;
    private readonly MongoEventStoreOptions _options;
    private readonly IMongoSequencedAppender<BsonDocument, AppendContext> _sequencedAppender;
    private readonly IMongoCollection<BsonDocument> _eventLog;
    private readonly IEventCodec<TEvent, BsonValue, BsonValue> _eventCodec;
    private readonly ILogger? _logger;
    private readonly RefCountedChangeStreamSubject<ChangeStreamDocument<BsonDocument>> _allEventsSubject;

    internal MongoEventStore(
        IMongoClient client,
        IDisposable? ownedClient,
        MongoEventStoreOptions options,
        IMongoSequencedAppender<BsonDocument, AppendContext> sequencedAppender,
        IMongoCollection<BsonDocument> eventLog,
        IEventCodec<TEvent, BsonValue, BsonValue> eventCodec,
        ILogger? logger = null)
    {
        _client = client;
        _ownedClient = ownedClient;
        _options = options;
        _sequencedAppender = sequencedAppender;
        _eventCodec = eventCodec;
        _logger = logger;

        _eventLog = eventLog
            .WithReadConcern(ReadConcern.Majority)
            .WithReadPreference(ReadPreference.Primary);

        _allEventsSubject = CreateAllEventsSubject(_eventLog, logger);
    }

    /// <summary>
    /// Creates the event log's indexes if they do not already exist. Idempotent, so it can run on every start-up.
    /// </summary>
    public Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        return MongoEventStoreAdmin.EnsureInitializedAsync(_client, _options, cancellationToken);
    }

    public async Task AppendAsync(
        string streamId,
        IEnumerable<AppendableEvent<TEvent>> events,
        ExpectedStreamState<StreamPosition> expectedState = default,
        Guid? commitId = null,
        AppendOptions? options = null,
        CancellationToken cancellationToken = default)
    {
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

        var context = new AppendContext(commitId.Value, streamId, expectedState);
        var appendOptions = new DomainBlocks.MongoDB.Sequencing.AppendOptions { Timeout = options.Timeout };

        await _sequencedAppender
            .AppendAsync(eventDocuments, context, appendOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    public IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadAll(
        ReadDirection direction = ReadDirection.Forward,
        ReadOrigin<LogPosition> origin = default,
        ReadAllOptions? options = null)
    {
        return Impl();

        async IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> Impl(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            origin = origin.ResolveFor(direction);
            options ??= ReadAllOptions.Default;

            if (direction.ProducesEmptyReadFrom(origin))
                yield break;

            var query = GetReadQuery(direction, origin, EventLogEntry.FieldNames.Position);

            using var cursor = await _eventLog
                .Find(query.Filter)
                .Sort(query.Sort)
                .Limit(options.MaxCount)
                .SetExcludeMetadata(!options.IncludeMetadata)
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
        ReadOrigin<StreamPosition> origin = default,
        ReadStreamOptions? options = null)
    {
        return Impl();

        async IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> Impl(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            origin = origin.ResolveFor(direction);
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
                .SetExcludeMetadata(!options.IncludeMetadata)
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

    public IAsyncEnumerable<SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>> SubscribeToAll(
        SubscriptionOrigin<LogPosition> origin = default,
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

    public IAsyncEnumerable<SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>> SubscribeToStream(
        string streamId,
        SubscriptionOrigin<StreamPosition> origin = default,
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

    public async ValueTask DisposeAsync()
    {
        await _sequencedAppender.DisposeAsync().ConfigureAwait(false);
        _ownedClient?.Dispose();
    }

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
        var forward = direction == ReadDirection.Forward;

        var sort = forward
            ? Builders<BsonDocument>.Sort.Ascending(positionFieldName)
            : Builders<BsonDocument>.Sort.Descending(positionFieldName);

        // Read origins are inclusive. A read from either end of the sequence is bounded only by its direction; the
        // caller has already returned for the two combinations that read nothing. TPos is a type parameter here, so
        // the position is read with TryGetValue: a pattern cannot bind a variable to a case that mentions one.
        var filter = Builders<BsonDocument>.Filter.Empty;

        if (origin.TryGetValue(out ReadOrigin<TPos>.At at))
        {
            filter = forward
                ? Builders<BsonDocument>.Filter.Gte(positionFieldName, at.Position.Value)
                : Builders<BsonDocument>.Filter.Lte(positionFieldName, at.Position.Value);
        }

        return new ReadQuery(filter, sort);
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