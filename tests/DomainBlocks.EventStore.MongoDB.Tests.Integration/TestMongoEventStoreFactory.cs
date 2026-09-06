using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Abstractions.Codecs;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing.Integration;
using DomainBlocks.Testing.Integration.MongoDB;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

public static class TestMongoEventStoreFactory
{
    public static TestMongoEventStoreFactory<object> CreateDefault(MongoEventStoreOptions options)
    {
        var eventTypeMap = EventTypeMap.Create(EventTypeMapping.ReadWrite<TestEvent>());

        return new TestMongoEventStoreFactory<object>(
            TestMongoConnectionStrings.Default,
            options,
            eventTypeMap);
    }
}

public sealed class TestMongoEventStoreFactory<TEvent> :
    ITestEventStoreFactory<TEvent, string, StreamPosition, LogPosition>
    where TEvent : notnull
{
    private readonly MongoEventStoreOptions _options;
    private readonly EventCodec<TEvent, BsonValue, BsonValue> _codec;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<TestMongoEventStoreFactory<TEvent>> _logger;
    private readonly Task _initTask;

    public TestMongoEventStoreFactory(
        string connectionString,
        MongoEventStoreOptions options,
        EventTypeMap eventTypeMap)
    {
        _options = options;
        _codec = TestMongoEventCodec.Create<TEvent>(eventTypeMap);

        _loggerFactory = LoggerFactory.Create(x => x
            .AddSimpleConsole(opt =>
            {
                opt.IncludeScopes = true;
                opt.TimestampFormat = "HH:mm:ss.fff ";
                opt.SingleLine = true;
            })
            .SetMinimumLevel(LogLevel.Debug));

        _logger = _loggerFactory.CreateLogger<TestMongoEventStoreFactory<TEvent>>();

        var mongoClient = new MongoClient(connectionString);
        _initTask = MongoEventStoreAdmin.EnsureInitializedAsync(mongoClient, _options);

        MongoClient = mongoClient;
    }

    public IMongoClient MongoClient { get; }

    public async Task<ITestEventStoreHandle<TEvent, string, StreamPosition, LogPosition>> CreateAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        await _initTask.WaitAsync(cancellationToken);

        var logger = _loggerFactory.CreateLogger($"MongoEventStore_{name}");

        var handle = new EventStoreHandle(
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

    private sealed class EventStoreHandle(
        IMongoClient mongoClient,
        EventCodec<TEvent, BsonValue, BsonValue> codec,
        MongoEventStoreOptions options,
        ILogger logger) :
        ITestEventStoreHandle<TEvent, string, StreamPosition, LogPosition>
    {
        private readonly MongoEventStore<TEvent>
            _instance = MongoEventStore.Create(mongoClient, codec, options, logger);

        public IEventStore<TEvent, string, StreamPosition, LogPosition> Instance => _instance;

        public ValueTask DisposeAsync() => _instance.DisposeAsync();
    }
}