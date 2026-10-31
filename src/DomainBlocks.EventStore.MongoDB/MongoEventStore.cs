using System.Diagnostics;
using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Filtering.Nodes;
using DomainBlocks.EventStore.Internal;
using DomainBlocks.EventStore.Internal.Feeds;
using DomainBlocks.EventStore.Internal.Subscriptions;
using DomainBlocks.EventStore.MongoDB.Feeds;
using DomainBlocks.MongoDB.Sequencing;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using static DomainBlocks.EventStore.MongoDB.EventLogEntry;

namespace DomainBlocks.EventStore.MongoDB;

public static class MongoEventStore
{
    internal const string SequenceId = "event_log_seq";

    private static readonly StringFieldDefinition<BsonDocument, long> SequenceTargetField =
        new(FieldNames.Position);

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
            SequenceId,
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
                MaxBatchSize = options.AppendMaxBatchSize
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
/// Represents an event store backed by MongoDB. Create one with <see cref="MongoEventStoreBuilder{TEvent}"/>.
/// </summary>
/// <remarks>
/// Disposing the store disposes its client only if the builder created it. A subscription that is live when the store
/// is disposed throws <see cref="ObjectDisposedException"/>.
/// </remarks>
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
    private readonly RefCountedFeed<EventLogDocument<TEvent>, BsonTimestamp> _feed;

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

        _feed = CreateFeed(_eventLog, logger);
    }

    /// <summary>
    /// Creates the event log's indexes and the sequence document that appends claim log positions from, if they do not
    /// exist. Idempotent, so it can run at every startup. Run it before the first append.
    /// </summary>
    public Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        return MongoEventStoreAdmin.EnsureInitializedAsync(_client, _options, cancellationToken);
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

        // The sequencing library passes the timeout to CancelAfter without checking it.
        CancellationTokenSource.ThrowIfDelayOutOfRange(options.Timeout);

        var bsonStreamId = new BsonString(streamId);
        var bsonCommitId = new BsonBinaryData(commitId.Value, GuidRepresentation.Standard);

        var eventDocuments = _eventCodec
            .Encode(events)
            .Select((x, i) => new BsonDocument
            {
                { FieldNames.StreamId, bsonStreamId },
                { FieldNames.CommitId, bsonCommitId },
                { FieldNames.CommitIndex, i },
                { FieldNames.EventName, x.EventName },
                { FieldNames.EventData, x.EventData },
                { FieldNames.Metadata, x.Metadata ?? BsonNull.Value }
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
        var eventFilter = TranslateFilter(options?.Filter);

        return Impl();

        async IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> Impl(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            origin ??= direction == ReadDirection.Forward ? ReadOrigin.Start : ReadOrigin.End;
            options ??= ReadAllOptions.Default;

            if (direction.ProducesEmptyReadFrom(origin))
                yield break;

            var query = GetReadQuery(direction, origin, FieldNames.Position);
            var filter = eventFilter is null ? query.Filter : query.Filter & eventFilter;

            using var cursor = await _eventLog
                .Find(filter)
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
        ReadOrigin<StreamPosition>? origin = null,
        ReadStreamOptions? options = null)
    {
        var eventFilter = TranslateFilter(options?.Filter);

        return Impl();

        async IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> Impl(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            origin ??= direction == ReadDirection.Forward ? ReadOrigin.Start : ReadOrigin.End;
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

            var query = GetReadQuery(direction, origin, FieldNames.StreamPosition);
            var filter = query.Filter & Builders<BsonDocument>.Filter.Eq(FieldNames.StreamId, streamId);

            if (eventFilter is not null)
                filter &= eventFilter;

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
        SubscriptionOrigin<LogPosition>? origin = null,
        SubscriptionOptions? options = null)
    {
        options ??= SubscriptionOptions.Default;
        var catchUpFilter = TranslateFilter(options.Filter) ?? Builders<BsonDocument>.Filter.Empty;
        var correlationId = CorrelationId.ReserveOrGenerate(options.CorrelationId);

        return new SubscriptionAsyncEnumerable<TEvent, EventLogDocument<TEvent>, BsonTimestamp, LogPosition>(
            _feed,
            new MongoCatchUpReader<TEvent, LogPosition>(
                _eventLog,
                _eventCodec,
                Builders<BsonDocument>.Filter.Empty,
                catchUpFilter,
                FieldNames.Position,
                options,
                correlationId,
                _logger),
            options.Filter,
            origin,
            options.QueueCapacity,
            correlationId,
            _logger);
    }

    public IAsyncEnumerable<SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>> SubscribeToStream(
        string streamId,
        SubscriptionOrigin<StreamPosition>? origin = null,
        SubscriptionOptions? options = null)
    {
        options ??= SubscriptionOptions.Default;
        var streamFilter = Builders<BsonDocument>.Filter.Eq(FieldNames.StreamId, streamId);
        var translatedFilter = TranslateFilter(options.Filter);
        var catchUpFilter = translatedFilter is null ? streamFilter : streamFilter & translatedFilter;
        var correlationId = CorrelationId.ReserveOrGenerate(options.CorrelationId);

        return new SubscriptionAsyncEnumerable<TEvent, EventLogDocument<TEvent>, BsonTimestamp, StreamPosition>(
            _feed,
            new MongoCatchUpReader<TEvent, StreamPosition>(
                _eventLog,
                _eventCodec,
                streamFilter,
                catchUpFilter,
                FieldNames.StreamPosition,
                options,
                correlationId,
                _logger),
            EventFilter.StreamIds(streamId) & options.Filter,
            origin,
            options.QueueCapacity,
            correlationId,
            _logger);
    }

    // A filter is translated when the method is called, so a filter that cannot be translated is refused there rather
    // than on enumeration. A read or catch-up without a filter has no further condition, so it runs exactly the query
    // it would run if there were no filters.
    private static FilterDefinition<BsonDocument>? TranslateFilter(EventFilter? filter) =>
        filter is null or AllEventsFilter ? null : MongoFilterTranslator.Translate(filter);

    public async ValueTask DisposeAsync()
    {
        await _sequencedAppender.DisposeAsync().ConfigureAwait(false);

        // The feed's change stream runs on the client, so the feed stops before the client is disposed.
        await _feed.DisposeAsync().ConfigureAwait(false);
        _ownedClient?.Dispose();
    }

    // Each feed has its own live document, which it sets to each inserted document and hands to every subscription.
    private RefCountedFeed<EventLogDocument<TEvent>, BsonTimestamp> CreateFeed(
        IMongoCollection<BsonDocument> eventLog,
        ILogger? logger)
    {
        var insertsOnly = Builders<ChangeStreamDocument<BsonDocument>>.Filter.Eq(
            x => x.OperationType,
            ChangeStreamOperationType.Insert);

        var pipeline = new EmptyPipelineDefinition<ChangeStreamDocument<BsonDocument>>().Match(insertsOnly);
        var resumePolicy = new FeedResumePolicy(ChangeStreamTransientErrors.IsTransient, logger: logger);

        return new RefCountedFeed<EventLogDocument<TEvent>, BsonTimestamp>(() =>
        {
            var liveDocument = new EventLogDocument<TEvent>(_eventCodec);

            return ChangeStreamFeed.Create(
                eventLog.Database.Client,
                eventLog.WatchAsync,
                pipeline,
                static change => change.ResumeToken,
                static change => change.ClusterTime,
                change =>
                {
                    liveDocument.Set(change.FullDocument);
                    return liveDocument;
                },
                resumePolicy,
                logger);
        });
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
        var filter = Builders<BsonDocument>.Filter.Eq(FieldNames.StreamId, streamId);
        return await _eventLog.Find(filter).AnyAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed class ReadQuery(FilterDefinition<BsonDocument> filter, SortDefinition<BsonDocument> sort)
    {
        public FilterDefinition<BsonDocument> Filter { get; } = filter;
        public SortDefinition<BsonDocument> Sort { get; } = sort;
    }
}