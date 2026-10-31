using System.Collections.Concurrent;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Internal;
using DomainBlocks.EventStore.Internal.Feeds;
using DomainBlocks.EventStore.Internal.Subscriptions;
using DomainBlocks.EventStore.MongoDB.Feeds;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.MongoDB.Bson;
using DomainBlocks.Testing.Events;
using DomainBlocks.Testing.Integration.EventStore;
using DomainBlocks.Testing.Integration.EventStore.MongoDB;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Events;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

/// <summary>
/// Builds a subscription from its constituent components against an isolated event store, exposing controls for testing
/// catch-up, live delivery, and change stream failures.
/// </summary>
internal sealed class CatchUpScenario : IAsyncDisposable
{
    private readonly MongoEventStoreOptions _options;
    private readonly IEventStore<object, string, StreamPosition, LogPosition> _eventStore;
    private readonly IMongoCollection<BsonDocument> _catchUpEventLog;
    private readonly IEventCodec<object, BsonValue, BsonValue> _codec;
    private readonly RefCountedFeed<EventLogDocument<object>, BsonTimestamp> _feed;
    private readonly MongoClient _catchUpClient;
    private readonly ConcurrentQueue<CatchUpRead> _catchUpReads = new();
    private readonly ConcurrentQueue<BsonTimestamp> _catchUpMarks = new();
    private readonly CancellationToken _cancellationToken;

    private CatchUpScenario(MongoEventStoreOptions options, CancellationToken cancellationToken)
    {
        _options = options;
        _cancellationToken = cancellationToken;

        var typeMap = new EventTypeMapBuilder().Add<TestEvent>().Build();

        var codec = EventCodec.Create(new EventCodecOptions<object, BsonValue, BsonValue>
        {
            TypeMap = typeMap,
            EventSerializer = new BsonDocumentObjectSerializer(),
            MetadataSerializer = new BsonDocumentMetadataSerializer()
        });

        _eventStore = new MongoEventStoreBuilder<object>()
            .UseClient(MongoTestEnvironment.MongoClient)
            .UseOptions(options)
            .UseCodec(codec)
            .Build();

        var eventLog = MongoTestEnvironment.MongoClient
            .GetDatabase(options.DatabaseName)
            .GetCollection<BsonDocument>(options.EventLogCollectionName)
            .WithReadConcern(ReadConcern.Majority)
            .WithReadPreference(ReadPreference.Primary);

        var insertsOnly = Builders<ChangeStreamDocument<BsonDocument>>.Filter.Eq(
            x => x.OperationType,
            ChangeStreamOperationType.Insert);

        // Catch-ups read through a client of their own, so each of their commands can be recorded.
        var settings = MongoClientSettings.FromConnectionString(MongoTestEnvironment.ConnectionString);
        settings.ClusterConfigurator = builder => builder.Subscribe<CommandStartedEvent>(RecordCatchUpCommand);
        _catchUpClient = new MongoClient(settings);

        _codec = codec;
        _catchUpEventLog = GetEventLog(_catchUpClient);
        ChangeStreamProbe = new ChangeStreamProbe(eventLog);

        var pipeline = new EmptyPipelineDefinition<ChangeStreamDocument<BsonDocument>>().Match(insertsOnly);
        var resumePolicy = new FeedResumePolicy(ChangeStreamTransientErrors.IsTransient);

        var logger = MongoTestEnvironment.LoggerFactory.CreateLogger("EventLogFeed");

        _feed = new RefCountedFeed<EventLogDocument<object>, BsonTimestamp>(() =>
        {
            var liveDocument = new EventLogDocument<object>(codec);

            return ChangeStreamFeed.Create(
                eventLog.Database.Client,
                ChangeStreamProbe.WatchAsync,
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

    public ChangeStreamProbe ChangeStreamProbe { get; }

    /// <summary>
    /// Gets each read of the events that a catch-up sent to the server, in order.
    /// </summary>
    public IReadOnlyList<CatchUpRead> CatchUpReads => _catchUpReads.ToArray();

    /// <summary>
    /// Gets the mark that each catch-up's read of the end waited for, in order.
    /// </summary>
    public IReadOnlyList<BsonTimestamp> CatchUpMarks => _catchUpMarks.ToArray();

    public static async Task<CatchUpScenario> StartAsync(CancellationToken cancellationToken)
    {
        var options = new MongoEventStoreOptions { DatabaseName = $"catchup_{Guid.NewGuid():N}" };
        await MongoEventStoreAdmin.EnsureInitializedAsync(MongoTestEnvironment.MongoClient, options, cancellationToken);

        return new CatchUpScenario(options, cancellationToken);
    }

    public async Task<TestEvent[]> AppendAsync(string prefix, int count, string streamId = "s1")
    {
        var events = Enumerable
            .Range(0, count)
            .Select(i => new TestEvent { Value = $"{prefix}-{i}" })
            .ToArray();

        foreach (var e in events)
        {
            await _eventStore.AppendAsync(
                streamId,
                [AppendableEvent.Create<object>(e)],
                cancellationToken: _cancellationToken);
        }

        return events;
    }

    /// <summary>
    /// Appends events and waits until the change stream has delivered them.
    /// </summary>
    /// <remarks>
    /// When this method returns, all events appended by this call, and any earlier events in the change stream have
    /// been delivered to the stream's subscribers.
    /// </remarks>
    public async Task<TestEvent[]> AppendLiveAsync(string prefix, int count, string streamId = "s1")
    {
        var total = ChangeStreamProbe.DeliveredChangeCount + count;
        var events = await AppendAsync(prefix, count, streamId);
        await ChangeStreamProbe.WaitForDeliveredChangesAsync(total, _cancellationToken);

        return events;
    }

    /// <summary>
    /// Creates a subscription from the specified origin, or from the beginning of the event log if there is none.
    /// </summary>
    /// <remarks>
    /// The catch-up filter controls which events are read during catch-up, while the live filter controls which events
    /// are delivered from the change stream. Both filters must be specified to test equivalent filtering across the
    /// transition from catch-up to live delivery.
    /// </remarks>
    public SubscriptionProbe<StreamPosition, LogPosition> SubscribeToAll(
        SubscriptionOptions? options = null,
        FilterDefinition<BsonDocument>? catchUpFilter = null,
        EventFilter? liveFilter = null,
        SubscriptionOrigin<LogPosition>? origin = null)
    {
        options ??= SubscriptionOptions.Default;

        var correlationId = CorrelationId.ReserveOrGenerate(options.CorrelationId);
        var logger = MongoTestEnvironment.LoggerFactory.CreateLogger("Subscription");

        var subscription = new SubscriptionAsyncEnumerable<
            object,
            EventLogDocument<object>,
            BsonTimestamp,
            LogPosition>(
            _feed,
            new MongoCatchUpReader<object, LogPosition>(
                _catchUpEventLog,
                _codec,
                Builders<BsonDocument>.Filter.Empty,
                catchUpFilter ?? Builders<BsonDocument>.Filter.Empty,
                EventLogEntry.FieldNames.Position,
                options,
                correlationId,
                logger),
            liveFilter ?? EventFilter.All,
            origin ?? SubscriptionOrigin.Start,
            options.QueueCapacity,
            correlationId,
            logger);

        return new SubscriptionProbe<StreamPosition, LogPosition>(subscription, _cancellationToken);
    }

    /// <summary>
    /// Creates a snapshot reader of the sequence that the given filter selects, reading through the given client, or
    /// the scenario's if there is none.
    /// </summary>
    public MongoCatchUpReader<object, LogPosition> CreateCatchUpReader(
        FilterDefinition<BsonDocument> sequenceFilter,
        IMongoClient? client = null)
    {
        return new MongoCatchUpReader<object, LogPosition>(
            client is null ? _catchUpEventLog : GetEventLog(client),
            _codec,
            sequenceFilter,
            sequenceFilter,
            EventLogEntry.FieldNames.Position,
            SubscriptionOptions.Default,
            "sub-1",
            logger: null);
    }

    /// <summary>
    /// Kills the cursors of the finds on the event log that wait for a getMore, as a primary that restarts loses them,
    /// so a replay that reads its next batch from one fails.
    /// </summary>
    public async Task KillReplayCursorsAsync()
    {
        var idleFindCursors = PipelineDefinition<NoPipelineInput, BsonDocument>.Create(
            new BsonDocument("$currentOp", new BsonDocument("idleCursors", true)),
            new BsonDocument("$match", new BsonDocument
            {
                { "type", "idleCursor" },
                { "ns", $"{_options.DatabaseName}.{_options.EventLogCollectionName}" },
                { "cursor.originatingCommand.find", _options.EventLogCollectionName }
            }));

        using var found = await MongoTestEnvironment.MongoClient
            .GetDatabase("admin")
            .AggregateAsync(idleFindCursors, cancellationToken: _cancellationToken);

        var cursors = await found.ToListAsync(_cancellationToken);
        var cursorIds = new BsonArray(cursors.Select(x => x["cursor"]["cursorId"]));

        if (cursorIds.Count == 0)
            throw new InvalidOperationException("No replay cursor was waiting for a getMore.");

        var result = await MongoTestEnvironment.MongoClient
            .GetDatabase(_options.DatabaseName)
            .RunCommandAsync<BsonDocument>(
                new BsonDocument { { "killCursors", _options.EventLogCollectionName }, { "cursors", cursorIds } },
                cancellationToken: _cancellationToken);

        if (result["cursorsKilled"].AsBsonArray.Count != cursorIds.Count)
            throw new InvalidOperationException($"Not every replay cursor was killed: {result}");
    }

    public async ValueTask DisposeAsync()
    {
        await _eventStore.DisposeAsync();
        _catchUpClient.Dispose();
        await MongoTestEnvironment.MongoClient.DropDatabaseAsync(_options.DatabaseName);
    }

    private IMongoCollection<BsonDocument> GetEventLog(IMongoClient client)
    {
        return client
            .GetDatabase(_options.DatabaseName)
            .GetCollection<BsonDocument>(_options.EventLogCollectionName)
            .WithReadConcern(ReadConcern.Majority)
            .WithReadPreference(ReadPreference.Primary);
    }

    // A catch-up reads the end by sorting on position in descending order, and reads the events in ascending order.
    private void RecordCatchUpCommand(CommandStartedEvent e)
    {
        if (e.CommandName != "find")
            return;

        var position = EventLogEntry.FieldNames.Position;

        if (e.Command["sort"][position].ToInt32() < 0)
        {
            _catchUpMarks.Enqueue(e.Command["readConcern"]["afterClusterTime"].AsBsonTimestamp);
            return;
        }

        var range = e.Command["filter"][position];
        _catchUpReads.Enqueue(new CatchUpRead(range["$gt"].ToInt64(), range["$lte"].ToInt64()));
    }
}