using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing.Integration;
using DomainBlocks.Testing.Integration.MongoDB;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using NUnit.Framework;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

[TestFixture]
public class MongoEventStoreClientTests : EventStoreClientTests
{
    private MongoClient _mongoClient = null!;
    private MongoEventStoreNodeOptions _options = null!;
    private ILoggerFactory _loggerFactory = null!;
    private IEventStoreClient<IDomainEvent> _client = null!;
    private MongoEventStoreNode _node = null!;

    protected override IEventStoreClient<IDomainEvent> Client => _client;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        //_mongoClient = new MongoClient("mongodb+srv://dev:49afD4FXHDWUWpNg@domainblockstestcluster.cle2ydx.mongodb.net/?appName=DomainBlocksTestCluster");
        _mongoClient = new MongoClient(TestMongoConnectionStrings.Default);

        _options = new MongoEventStoreNodeOptions
        {
            DatabaseName = "domainblocks_tests"
        };

        _loggerFactory = LoggerFactory.Create(x => x
            .AddSimpleConsole(o => o.TimestampFormat = "HH:mm:ss.fff ")
            .SetMinimumLevel(LogLevel.Debug));

        _node = new MongoEventStoreNode(_mongoClient, _options, _loggerFactory);

        var eventTypeMap = EventTypeMap.Create(x => x.MapType<TestEvent>());
        var eventCodec = TestMongoEventCodec.Create<IDomainEvent>(eventTypeMap);
        _client = _node.CreateClient(eventCodec);

        await MongoEventStoreAdmin.EnsureInitializedAsync(_mongoClient, _options);

        await _node.StartAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _node.DisposeAsync();
        await _mongoClient.DropDatabaseAsync(_options.DatabaseName);

        _mongoClient.Dispose();
        _loggerFactory.Dispose();
    }
}