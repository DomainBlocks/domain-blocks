using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.MongoDB.ChangeStreams;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.MongoDB.Bson;
using DomainBlocks.Testing.Events;
using DomainBlocks.Testing.Integration.EventStore;
using DomainBlocks.Testing.Integration.EventStore.MongoDB;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

/// <summary>
/// A subscription built from its parts, as the store builds it, in a database of its own. The test can see each replay
/// that the subscription asks for and hold one back part-way, tell when the change stream has handed out live events,
/// and make the change stream fail.
/// </summary>
internal sealed class CatchUpScenario : IAsyncDisposable
{
    private readonly MongoEventStoreOptions _options;
    private readonly IEventStore<object, string, StreamPosition, LogPosition> _eventStore;
    private readonly EventLogReader<object> _reader;
    private readonly RefCountedChangeStreamSubject<EventLogDocument<object>> _subject;
    private readonly ReplayRecorder _replayRecorder = new();
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
            .ConfigureCodec(x => x.UseEventTypeMap(typeMap).UseEventSerializer(new BsonDocumentObjectSerializer()))
            .Build();

        var eventLog = MongoTestEnvironment.MongoClient
            .GetDatabase(options.DatabaseName)
            .GetCollection<BsonDocument>(options.EventLogCollectionName)
            .WithReadConcern(ReadConcern.Majority)
            .WithReadPreference(ReadPreference.Primary);

        var insertsOnly = Builders<ChangeStreamDocument<BsonDocument>>.Filter.Eq(
            x => x.OperationType,
            ChangeStreamOperationType.Insert);

        var liveDocument = new EventLogDocument<object>(codec);

        _reader = new EventLogReader<object>(eventLog, codec);
        ChangeStream = new InstrumentedChangeStream(eventLog);

        _subject = RefCountedChangeStreamSubject.Create(
            eventLog.Database.Client,
            ChangeStream.WatchAsync,
            new EmptyPipelineDefinition<ChangeStreamDocument<BsonDocument>>().Match(insertsOnly),
            static change => change.ResumeToken,
            change =>
            {
                liveDocument.Set(change.FullDocument);
                return liveDocument;
            },
            logger: MongoTestEnvironment.LoggerFactory.CreateLogger("ChangeStreamSubject"));
    }

    public InstrumentedChangeStream ChangeStream { get; }

    public IReadOnlyList<ReplayRecorder.Replay> Replays => _replayRecorder.Replays;

    public static async Task<CatchUpScenario> StartAsync(CancellationToken cancellationToken)
    {
        var options = new MongoEventStoreOptions { DatabaseName = $"catchup_{Guid.NewGuid():N}" };
        await MongoEventStoreAdmin.EnsureInitializedAsync(MongoTestEnvironment.MongoClient, options);

        return new CatchUpScenario(options, cancellationToken);
    }

    public async Task<TestEvent[]> AppendAsync(string prefix, int count, string streamId = "s1")
    {
        var events = Enumerable.Range(0, count).Select(i => new TestEvent { Value = $"{prefix}-{i}" }).ToArray();

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
    /// Appends events and waits until the change stream has handed them to every subscription attached to it. Live
    /// events appended earlier must already have been handed out.
    /// </summary>
    public async Task<TestEvent[]> AppendLiveAsync(string prefix, int count, string streamId = "s1")
    {
        var delivered = ChangeStream.DeliveredCount + count;
        var events = await AppendAsync(prefix, count, streamId);
        await ChangeStream.DeliveredAsync(delivered, _cancellationToken);

        return events;
    }

    /// <summary>
    /// Subscribes to the log from its start. The store turns a subscription filter into a query for the replay and an
    /// event filter for live events, so a filtered subscription takes both.
    /// </summary>
    public SubscriptionProbe<StreamPosition, LogPosition> SubscribeToAll(
        SubscriptionOptions? options = null,
        FilterDefinition<BsonDocument>? catchUpFilter = null,
        EventFilter? liveFilter = null)
    {
        var filter = catchUpFilter ?? Builders<BsonDocument>.Filter.Empty;

        var subscription = new SubscriptionAsyncEnumerable<object, LogPosition>(
            _reader,
            _subject,
            (reader, session, after, highWaterMark, ct) => _replayRecorder.Record(
                reader.ReadCatchUpAsync(session, filter, EventLogEntry.FieldNames.Position, after, highWaterMark, ct),
                after,
                highWaterMark,
                ct),
            static async (reader, session, ct) => await reader.GetLastPositionAsync(
                session,
                Builders<BsonDocument>.Filter.Empty,
                EventLogEntry.FieldNames.Position,
                ct) is { } pos
                ? LogPosition.FromInt64(pos)
                : null,
            liveFilter ?? EventFilter.All,
            static ctx => ctx.LogPosition,
            SubscriptionOrigin.Start,
            options ?? new SubscriptionOptions(),
            MongoTestEnvironment.LoggerFactory.CreateLogger("Subscription"));

        return new SubscriptionProbe<StreamPosition, LogPosition>(subscription, _cancellationToken);
    }

    /// <summary>
    /// Holds the replay with the given index once it has delivered its first event, until the hold is released.
    /// </summary>
    public ReplayRecorder.Hold HoldReplay(int replayIndex) => _replayRecorder.HoldReplay(replayIndex);

    public async ValueTask DisposeAsync()
    {
        await _eventStore.DisposeAsync();
        await MongoTestEnvironment.MongoClient.DropDatabaseAsync(_options.DatabaseName);
    }
}