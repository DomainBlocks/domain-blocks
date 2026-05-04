using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.MongoDB.Sequencing;
using DomainBlocks.Testing.Integration;
using DomainBlocks.Testing.Integration.MongoDB;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

public static class TestMongoEventStoreClient2Factory
{
    public static TestMongoEventStoreClient2Factory<object> CreateDefault(MongoEventStoreClientOptions2 options)
    {
        var eventTypeMap = EventTypeMap.Create(x => x.MapType<TestEvent>());

        return new TestMongoEventStoreClient2Factory<object>(
            TestMongoConnectionStrings.Default,
            options,
            eventTypeMap);
    }
}

public sealed class TestMongoEventStoreClient2Factory<TEvent> :
    ITestEventStoreClientFactory<TEvent>
    where TEvent : notnull
{
    private readonly MongoEventStoreClientOptions2 _options;
    private readonly EventCodec<TEvent, BsonValue, BsonValue> _codec;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<TestMongoEventStoreClient2Factory<TEvent>> _logger;
    private readonly Task _initTask;

    public TestMongoEventStoreClient2Factory(
        string connectionString,
        MongoEventStoreClientOptions2 options,
        EventTypeMap eventTypeMap)
    {
        _options = options;
        _codec = TestMongoEventCodec.Create<TEvent>(eventTypeMap);

        _loggerFactory = LoggerFactory.Create(x => x
            .AddSimpleConsole(opt =>
            {
                opt.IncludeScopes = true;
                opt.TimestampFormat = "HH:mm:ss.fff ";
            })
            .SetMinimumLevel(LogLevel.Trace));

        _logger = _loggerFactory.CreateLogger<TestMongoEventStoreClient2Factory<TEvent>>();

        var mongoClient = new MongoClient(connectionString);
        _initTask = MongoEventStoreAdmin2.EnsureInitializedAsync(mongoClient, _options);

        MongoClient = mongoClient;
    }

    public IMongoClient MongoClient { get; }

    public async Task<ITestEventStoreClientHandle<TEvent>> CreateAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        await _initTask.WaitAsync(cancellationToken);

        var logger = _loggerFactory.CreateLogger($"MongoEventStoreClient2_{name}");

        var handle = new ClientHandle(
            MongoClient,
            _codec,
            _options,
            logger);

        return handle;
    }

    public async ValueTask DisposeAsync()
    {
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            await MongoClient.DropDatabaseAsync(_options.DatabaseName, ct.Token);
        }
        catch (OperationCanceledException) when (ct.Token.IsCancellationRequested)
        {
            _logger.LogWarning("Timed out waiting for database '{DatabaseName}' to be dropped", _options.DatabaseName);
        }

        MongoClient.Dispose();
        _loggerFactory.Dispose();
    }

    private sealed class ClientHandle : ITestEventStoreClientHandle<TEvent>
    {
        private readonly MongoEventStoreClient2<TEvent> _client;

        public ClientHandle(
            IMongoClient mongoClient,
            EventCodec<TEvent, BsonValue, BsonValue> codec,
            MongoEventStoreClientOptions2 options,
            ILogger logger)
        {
            var db = mongoClient.GetDatabase(options.DatabaseName);

            var sequenceBinding = new MongoSequenceBinding<BsonDocument>(
                new CollectionNamespace(db.DatabaseNamespace, options.SequencesCollectionName),
                new CollectionNamespace(db.DatabaseNamespace, options.EventLogCollectionName),
                sequenceId: "event_log_seq",
                targetField: "_id");

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

            _client = new MongoEventStoreClient2<TEvent>(sequencedAppender, eventLog, codec);
        }

        public IEventStoreClient<TEvent> Client => _client;

        public ValueTask DisposeAsync() => _client.DisposeAsync();
    }
}