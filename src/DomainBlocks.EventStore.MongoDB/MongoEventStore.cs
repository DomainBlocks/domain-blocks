using System.Diagnostics;
using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Filtering.Nodes;
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
public sealed class MongoEventStore<TEvent> :
    IEventStore<TEvent, string, StreamPosition, LogPosition>,
    IEventFilterExplainer
    where TEvent : notnull
{
    private readonly IMongoClient _client;
    private readonly IDisposable? _ownedClient;
    private readonly MongoEventStoreOptions _options;

    // As it was when the store was built, which is what its change stream is filtered by. The options can be set
    // again, and a subscription that then caught up by another filter than it is sent live events by would miss
    // events without a word.
    private readonly EventFilter _subscriptionFilter;

    private readonly IMongoSequencedAppender<BsonDocument, AppendContext> _sequencedAppender;
    private readonly IMongoCollection<BsonDocument> _eventLog;
    private readonly IEventCodec<TEvent, BsonValue, BsonValue> _eventCodec;
    private readonly ILogger? _logger;
    private readonly RefCountedChangeStreamSubject<EventLogDocument<TEvent>> _allEventsSubject;

    // One for the store, set by the observers of its change stream for each change, which they are handed one
    // after another.
    private readonly EventLogDocument<TEvent> _liveDocument;

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
        _subscriptionFilter = options.SubscriptionFilter;
        _sequencedAppender = sequencedAppender;
        _eventCodec = eventCodec;
        _logger = logger;

        _eventLog = eventLog
            .WithReadConcern(ReadConcern.Majority)
            .WithReadPreference(ReadPreference.Primary);

        // Planned with pushdown required, so a filter that the server cannot evaluate the whole of is refused here.
        var subscriptionFilterPlan = EventFilterPlan.Create(
            _subscriptionFilter,
            FilterPushdownMode.Require,
            RefuseTypes,
            MongoFilterTranslator.CanPush);

        _allEventsSubject = CreateAllEventsSubject(_eventLog, subscriptionFilterPlan.Pushdown, logger);
        _liveDocument = new EventLogDocument<TEvent>(eventCodec, includeMetadata: true);
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
        options ??= ReadAllOptions.Default;

        // Planned here rather than as the read is enumerated, so that a filter that is refused is refused at the call.
        var filterPlan = PlanFilter(options.Filter, options.FilterPushdownMode);

        return Impl();

        async IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> Impl(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            origin ??= direction == ReadDirection.Forward ? ReadOrigin.Start : ReadOrigin.End;

            if (direction.ProducesEmptyReadFrom(origin))
                yield break;

            var query = GetReadQuery(direction, origin, EventLogEntry.FieldNames.Position);

            var events = FindAsync(
                query.Filter,
                query.Sort,
                options.MaxCount,
                options.IncludeMetadata,
                filterPlan,
                cancellationToken);

            await foreach (var e in events.ConfigureAwait(false))
                yield return e;
        }
    }

    public IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadStream(
        string streamId,
        ReadDirection direction = ReadDirection.Forward,
        ReadOrigin<StreamPosition>? origin = null,
        ReadStreamOptions? options = null)
    {
        options ??= ReadStreamOptions.Default;
        var filterPlan = PlanFilter(options.Filter, options.FilterPushdownMode);

        return Impl();

        async IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> Impl(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            origin ??= direction == ReadDirection.Forward ? ReadOrigin.Start : ReadOrigin.End;

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

            var events = FindAsync(
                filter,
                query.Sort,
                options.MaxCount,
                options.IncludeMetadata,
                filterPlan,
                cancellationToken);

            var isEmpty = true;

            await foreach (var e in events.ConfigureAwait(false))
            {
                isEmpty = false;
                yield return e;
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

        // Catching up is a filtered read. Live changes are not asked of the database, so each is tested against the
        // whole filter, with its types turned into names so that one can be passed over without being decoded.
        var filter = _subscriptionFilter & options.Filter;
        var filterPlan = PlanFilter(filter, options.FilterPushdownMode);
        var liveFilter = filter.LowerEventTypes(_eventCodec.ResolveEventNames);

        return new SubscriptionAsyncEnumerable<TEvent, LogPosition>(
            _eventLog,
            _allEventsSubject,
            _eventCodec,
            CatchUpFilter(Builders<BsonDocument>.Filter.Empty, filterPlan),
            filterPlan.Residual,
            streamId: null,
            liveFilter,
            EventLogEntry.FieldNames.Position,
            static ctx => ctx.LogPosition,
            origin,
            options,
            _logger);
    }

    public IAsyncEnumerable<SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>> SubscribeToStream(
        string streamId,
        SubscriptionOrigin<StreamPosition>? origin = null,
        SubscriptionOptions? options = null)
    {
        options ??= SubscriptionOptions.Default;

        var filter = _subscriptionFilter & options.Filter;
        var filterPlan = PlanFilter(filter, options.FilterPushdownMode);
        var liveFilter = filter.LowerEventTypes(_eventCodec.ResolveEventNames);
        var streamFilter = Builders<BsonDocument>.Filter.Eq(EventLogEntry.FieldNames.StreamId, streamId);

        return new SubscriptionAsyncEnumerable<TEvent, StreamPosition>(
            _eventLog,
            _allEventsSubject,
            _eventCodec,
            CatchUpFilter(streamFilter, filterPlan),
            filterPlan.Residual,
            streamId,
            liveFilter,
            EventLogEntry.FieldNames.StreamPosition,
            static ctx => ctx.StreamPosition,
            origin,
            options,
            _logger);
    }

    public EventFilterPlan ExplainFilter(EventFilter filter,
        FilterPushdownMode pushdownMode = FilterPushdownMode.Prefer)
    {
        ArgumentNullException.ThrowIfNull(filter);

        return PlanFilter(filter, pushdownMode);
    }

    public async ValueTask DisposeAsync()
    {
        await _sequencedAppender.DisposeAsync().ConfigureAwait(false);
        _ownedClient?.Dispose();
    }

    private RefCountedChangeStreamSubject<EventLogDocument<TEvent>> CreateAllEventsSubject(
        IMongoCollection<BsonDocument> eventLog,
        EventFilter subscriptionFilter,
        ILogger? logger)
    {
        return RefCountedChangeStreamSubject.Create(
            eventLog.Database.Client,
            eventLog.WatchAsync,
            MongoFilterTranslator.ToChangeStreamPipeline(subscriptionFilter),
            doc => doc.ResumeToken,
            doc =>
            {
                _liveDocument.Set(doc.FullDocument);
                return _liveDocument;
            },
            logger: logger);
    }

    private EventFilterPlan PlanFilter(EventFilter filter, FilterPushdownMode pushdownMode)
    {
        var plan = EventFilterPlan.Create(
            filter,
            pushdownMode,
            _eventCodec.ResolveEventNames,
            MongoFilterTranslator.CanPush,
            _eventCodec.ResolveStoredPath);

        // The filters are given as they are, so that they are only written out if the message is.
        if (plan != EventFilterPlan.Unfiltered)
            _logger?.FilterPlanned(filter, plan.Pushdown, plan.Residual);

        return plan;
    }

    // The subscription filter of a store selects by what is stored beside the payload, as it does for every store: the
    // names that a type is read as are for a codec to say, and a database may keep a filter longer than a codec does.
    private static IReadOnlyCollection<string> RefuseTypes(Type eventType)
    {
        throw new EventFilterNotSupportedException(
            $"The subscription filter of a store cannot select by event type, and was given '{eventType}'. " +
            "Select by the names that the events are stored under instead.");
    }

    // Nothing, if there is nothing to ask the database for.
    private static FilterDefinition<BsonDocument>? CatchUpFilter(
        FilterDefinition<BsonDocument> query,
        EventFilterPlan filterPlan)
    {
        return filterPlan.Pushdown is NoEventsFilter ? null : WithPushedFilter(query, filterPlan);
    }

    // An unfiltered query is left as it is.
    private static FilterDefinition<BsonDocument> WithPushedFilter(
        FilterDefinition<BsonDocument> query,
        EventFilterPlan filterPlan)
    {
        return filterPlan.Pushdown is AllEventsFilter
            ? query
            : query & MongoFilterTranslator.Translate(filterPlan.Pushdown);
    }

    private async IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> FindAsync(
        FilterDefinition<BsonDocument> query,
        SortDefinition<BsonDocument> sort,
        int? maxCount,
        bool includeMetadata,
        EventFilterPlan filterPlan,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Nothing to ask the database for.
        if (filterPlan.Pushdown is NoEventsFilter)
            yield break;

        query = WithPushedFilter(query, filterPlan);

        var remainder = filterPlan.Residual is AllEventsFilter ? null : filterPlan.Residual;
        var remaining = maxCount;

        // The database cannot count the events that the remainder selects, so they are counted as they are returned.
        using var cursor = await _eventLog
            .Find(query)
            .Sort(sort)
            .Limit(remainder is null ? maxCount : null)
            .SetExcludeMetadata(!includeMetadata && !filterPlan.IsMetadataRequired)
            .ToCursorAsync(cancellationToken)
            .ConfigureAwait(false);

        // One for the whole read, set again for each document that comes back.
        var document = new EventLogDocument<TEvent>(_eventCodec, includeMetadata);

        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var doc in cursor.Current)
            {
                document.Set(doc);

                if (remainder is not null && !remainder.Matches(document))
                    continue;

                yield return document.DecodedEvent;

                if (remainder is not null && remaining is not null && --remaining == 0)
                    yield break;
            }
        }
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