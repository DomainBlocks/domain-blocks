using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing.Integration;
using DomainBlocks.Testing.Integration.MongoDB;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

public static class TestMongoEventStoreClientFactory
{
    public static TestMongoEventStoreClientFactory<object> CreateDefault(MongoEventStoreClientOptions options)
    {
        var eventTypeMap = EventTypeMap.Create(x => x.MapType<TestEvent>());

        return new TestMongoEventStoreClientFactory<object>(
            TestMongoConnectionStrings.Default,
            options,
            eventTypeMap);
    }
}

public sealed class TestMongoEventStoreClientFactory<TEvent> :
    ITestEventStoreClientFactory<TEvent>
    where TEvent : notnull
{
    private readonly MongoEventStoreClientOptions _options;
    private readonly EventCodec<TEvent, BsonValue, BsonValue> _codec;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<TestMongoEventStoreClientFactory<TEvent>> _logger;
    private readonly Task _initTask;

    public TestMongoEventStoreClientFactory(
        string connectionString,
        MongoEventStoreClientOptions options,
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
            .SetMinimumLevel(LogLevel.Debug));

        _logger = _loggerFactory.CreateLogger<TestMongoEventStoreClientFactory<TEvent>>();

        var mongoClient = new MongoClient(connectionString);
        _initTask = MongoEventStoreAdmin.EnsureInitializedAsync(mongoClient, _options);

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

    private sealed class ClientHandle(
        IMongoClient mongoClient,
        EventCodec<TEvent, BsonValue, BsonValue> codec,
        MongoEventStoreClientOptions options,
        ILogger logger) :
        ITestEventStoreClientHandle<TEvent>
    {
        private readonly MongoEventStoreClient<TEvent> _client =
            MongoEventStoreClient.Create(mongoClient, codec, options, logger);

        public IEventStoreClient<TEvent> Client => _client;

        public ValueTask DisposeAsync() => _client.DisposeAsync();
    }
}