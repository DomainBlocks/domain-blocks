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
/// Builds a subscription from its constituent components against an isolated event store, exposing controls for testing
/// catch-up, live delivery, and change stream failures.
/// </summary>
internal sealed class CatchUpScenario : IAsyncDisposable
{
    private readonly MongoEventStoreOptions _options;
    private readonly IEventStore<object, string, StreamPosition, LogPosition> _eventStore;
    private readonly EventLogReader<object> _reader;
    private readonly RefCountedChangeStreamSubject<EventLogDocument<object>> _subject;
    private readonly CatchUpRecorder _catchUpRecorder = new();
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

        var liveDocument = new EventLogDocument<object>(codec);

        _reader = new EventLogReader<object>(eventLog, codec);
        ChangeStreamProbe = new ChangeStreamProbe(eventLog);

        _subject = RefCountedChangeStreamSubject.Create(
            eventLog.Database.Client,
            ChangeStreamProbe.WatchAsync,
            new EmptyPipelineDefinition<ChangeStreamDocument<BsonDocument>>().Match(insertsOnly),
            static change => change.ResumeToken,
            change =>
            {
                liveDocument.Set(change.FullDocument);
                return liveDocument;
            },
            logger: MongoTestEnvironment.LoggerFactory.CreateLogger("ChangeStreamSubject"));
    }

    public ChangeStreamProbe ChangeStreamProbe { get; }

    public IReadOnlyList<CatchUpRead> CatchUpReads => _catchUpRecorder.Reads;

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
    /// Creates a subscription from the beginning of the event log.
    /// </summary>
    /// <remarks>
    /// The catch-up filter controls which events are read during catch-up, while the live filter controls which events
    /// are delivered from the change stream. Both filters must be specified to test equivalent filtering across the
    /// transition from catch-up to live delivery.
    /// </remarks>
    public SubscriptionProbe<StreamPosition, LogPosition> SubscribeToAll(
        SubscriptionOptions? options = null,
        FilterDefinition<BsonDocument>? catchUpFilter = null,
        EventFilter? liveFilter = null)
    {
        var filter = catchUpFilter ?? Builders<BsonDocument>.Filter.Empty;

        var subscription = new SubscriptionAsyncEnumerable<object, LogPosition>(
            _reader,
            _subject,
            (reader, session, from, to, ct) => _catchUpRecorder.Record(
                reader.ReadCatchUpAsync(session, filter, EventLogEntry.FieldNames.Position, from, to, ct),
                from,
                to,
                ct),
            static async (reader, session, ct) =>
            {
                var endPos = await reader.GetEndPositionAsync(
                    session,
                    Builders<BsonDocument>.Filter.Empty,
                    EventLogEntry.FieldNames.Position,
                    ct);

                return endPos is { } pos ? LogPosition.FromInt64(pos) : null;
            },
            liveFilter ?? EventFilter.All,
            static ctx => ctx.LogPosition,
            SubscriptionOrigin.Start,
            options,
            MongoTestEnvironment.LoggerFactory.CreateLogger("Subscription"));

        return new SubscriptionProbe<StreamPosition, LogPosition>(subscription, _cancellationToken);
    }

    /// <summary>
    /// Holds the catch-up with the given index once it has delivered its first event, until the hold is released.
    /// </summary>
    public CatchUpHold HoldCatchUp(int index) => _catchUpRecorder.Hold(index);

    public async ValueTask DisposeAsync()
    {
        await _eventStore.DisposeAsync();
        await MongoTestEnvironment.MongoClient.DropDatabaseAsync(_options.DatabaseName);
    }
}