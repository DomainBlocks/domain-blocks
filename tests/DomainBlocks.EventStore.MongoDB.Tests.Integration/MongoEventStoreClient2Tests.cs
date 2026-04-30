using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing.Integration;
using DomainBlocks.Testing.Integration.MongoDB;
using MongoDB.Driver;
using NUnit.Framework;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

[TestFixture]
public class MongoEventStoreClient2Tests : EventStoreClientTests
{
    private MongoClient _mongoClient = null!;
    private MongoEventStoreClientOptions2 _options = null!;
    private MongoEventStoreClient2<IDomainEvent> _client = null!;

    protected override IEventStoreClient<IDomainEvent> Client => _client;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _mongoClient = new MongoClient(MongoConnectionStrings.Default);

        _options = new MongoEventStoreClientOptions2
        {
            DatabaseName = "domainblocks_tests_v2"
        };

        var eventTypeMap = EventTypeMap.Create(x => x.MapType<TestEvent>());
        var eventCodec = MongoTestEventCodec.Create<IDomainEvent>(eventTypeMap);

        await MongoEventStoreAdmin2.EnsureInitializedAsync(_mongoClient, _options);

        _client = new MongoEventStoreClient2<IDomainEvent>(_mongoClient, eventCodec, _options);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _client.DisposeAsync();
        await _mongoClient.DropDatabaseAsync(_options.DatabaseName);
        _mongoClient.Dispose();
    }
}