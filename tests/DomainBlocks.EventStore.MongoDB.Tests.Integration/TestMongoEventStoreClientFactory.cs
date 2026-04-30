using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing.Integration;
using DomainBlocks.Testing.Integration.MongoDB;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

public sealed class TestMongoEventStoreClientFactory<TEvent> :
    ITestEventStoreClientFactory<TEvent>
    where TEvent : notnull
{
    private readonly MongoClient _mongoClient;
    private readonly MongoEventStoreNodeOptions _nodeOptions;
    private readonly EventTypeMap _eventTypeMap;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Task _initTask;

    public TestMongoEventStoreClientFactory(string connectionString, string databaseName, EventTypeMap eventTypeMap)
    {
        _mongoClient = new MongoClient(connectionString);

        _nodeOptions = new MongoEventStoreNodeOptions
        {
            DatabaseName = databaseName
        };

        _eventTypeMap = eventTypeMap;

        _loggerFactory = LoggerFactory.Create(x => x
            .AddSimpleConsole(o => o.TimestampFormat = "HH:mm:ss.fff ")
            .SetMinimumLevel(LogLevel.Debug));

        _initTask = MongoEventStoreAdmin.EnsureInitializedAsync(_mongoClient, _nodeOptions);
    }

    public async Task<ITestEventStoreClientHandle<TEvent>> CreateAsync(CancellationToken cancellationToken = default)
    {
        await _initTask.WaitAsync(cancellationToken);

        var handle = new ClientHandle(_mongoClient, _nodeOptions, _eventTypeMap);
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
            EventTypeMap eventTypeMap)
        {
            Node = new MongoEventStoreNode(mongoClient, nodeOptions);

            var eventCodec = TestMongoEventCodec.Create<TEvent>(eventTypeMap);
            Client = Node.CreateClient(eventCodec);
        }

        public IMongoEventStoreNode Node { get; }

        public IEventStoreClient<TEvent> Client { get; }

        public ValueTask DisposeAsync() => Node.DisposeAsync();
    }
}