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
    public static TestMongoEventStoreClientFactory<object> CreateDefault(MongoEventStoreNodeOptions options)
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
    private readonly MongoClient _mongoClient;
    private readonly MongoEventStoreNodeOptions _nodeOptions;
    private readonly EventCodec<TEvent, BsonValue, BsonValue> _codec;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Task _initTask;

    public TestMongoEventStoreClientFactory(
        string connectionString,
        MongoEventStoreNodeOptions nodeOptions,
        EventTypeMap eventTypeMap)
    {
        _mongoClient = new MongoClient(connectionString);
        _nodeOptions = nodeOptions;
        _codec = TestMongoEventCodec.Create<TEvent>(eventTypeMap);

        _loggerFactory = LoggerFactory.Create(x => x
            .AddSimpleConsole(o => o.TimestampFormat = "HH:mm:ss.fff ")
            .SetMinimumLevel(LogLevel.Debug));

        _initTask = MongoEventStoreAdmin.EnsureInitializedAsync(_mongoClient, _nodeOptions);
    }

    public async Task<ITestEventStoreClientHandle<TEvent>> CreateAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        await _initTask.WaitAsync(cancellationToken);

        var handle = new ClientHandle(_mongoClient, _nodeOptions, _codec);
        await handle.Node.StartAsync(cancellationToken);
        return handle;
    }

    public async ValueTask DisposeAsync()
    {
        await _mongoClient.DropDatabaseAsync(_nodeOptions.DatabaseName);

        _mongoClient.Dispose();
        _loggerFactory.Dispose();
    }

    private sealed class ClientHandle : ITestEventStoreClientHandle<TEvent>
    {
        public ClientHandle(
            IMongoClient mongoClient,
            MongoEventStoreNodeOptions nodeOptions,
            EventCodec<TEvent, BsonValue, BsonValue> codec)
        {
            Node = new MongoEventStoreNode(mongoClient, nodeOptions);
            Client = Node.CreateClient(codec);
        }

        public IMongoEventStoreNode Node { get; }

        public IEventStoreClient<TEvent> Client { get; }

        public ValueTask DisposeAsync() => Node.DisposeAsync();
    }
}