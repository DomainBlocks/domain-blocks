using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing.Integration;
using DomainBlocks.Testing.Integration.MongoDB;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

public sealed class TestMongoEventStoreClient2Factory<TEvent> :
    ITestEventStoreClientFactory<TEvent>
    where TEvent : notnull
{
    private readonly MongoClient _mongoClient;
    private readonly MongoEventStoreClientOptions2 _options;
    private readonly EventCodec<TEvent, BsonValue, BsonValue> _codec;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Task _initTask;

    public TestMongoEventStoreClient2Factory(string connectionString, string databaseName, EventTypeMap eventTypeMap)
    {
        _mongoClient = new MongoClient(connectionString);

        _options = new MongoEventStoreClientOptions2
        {
            DatabaseName = databaseName
        };

        _codec = TestMongoEventCodec.Create<TEvent>(eventTypeMap);

        _loggerFactory = LoggerFactory.Create(x => x
            .AddSimpleConsole(o => o.TimestampFormat = "HH:mm:ss.fff ")
            .SetMinimumLevel(LogLevel.Debug));

        _initTask = MongoEventStoreAdmin2.EnsureInitializedAsync(_mongoClient, _options);
    }

    public async Task<ITestEventStoreClientHandle<TEvent>> CreateAsync(CancellationToken cancellationToken = default)
    {
        await _initTask.WaitAsync(cancellationToken);

        var handle = new ClientHandle(_mongoClient, _codec, _options);
        return handle;
    }

    public async ValueTask DisposeAsync()
    {
        await _mongoClient.DropDatabaseAsync(_options.DatabaseName);

        _mongoClient.Dispose();
        _loggerFactory.Dispose();
    }

    private sealed class ClientHandle(
        IMongoClient mongoClient,
        EventCodec<TEvent, BsonValue, BsonValue> codec,
        MongoEventStoreClientOptions2 options) :
        ITestEventStoreClientHandle<TEvent>
    {
        private readonly MongoEventStoreClient2<TEvent> _client = new(mongoClient, codec, options);

        public IEventStoreClient<TEvent> Client => _client;

        public ValueTask DisposeAsync() => _client.DisposeAsync();
    }
}