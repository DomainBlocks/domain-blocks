using DomainBlocks.EventStore.MongoDB;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using NUnit.Framework;

namespace DomainBocks.Testing.Integration.MongoDB;

public class MongoEventStoreTestFixture
{
    static MongoEventStoreTestFixture()
    {
        BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
    }

    protected IMongoDatabase MongoDatabase { get; private set; } = null!;
    protected MongoEventStoreOptions<EventDocument> MongoEventStoreOptions { get; private set; } = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        var client = new MongoClient("mongodb://localhost:27017");
        MongoDatabase = client.GetDatabase("test");
        MongoEventStoreOptions = DomainBlocks.EventStore.MongoDB.MongoEventStoreOptions.CreateDefault();
        await MongoEventStoreAdmin.EnsureIndexesAsync(MongoDatabase, MongoEventStoreOptions);
    }
}

public class MongoEventStoreTestFixture<TPayload> : MongoEventStoreTestFixture where TPayload : notnull
{
    protected IMongoEventStore<TPayload> MongoEventStore { get; private set; } = null!;

    [OneTimeSetUp]
    public new void OneTimeSetUp()
    {
        MongoEventStore = DomainBlocks.EventStore.MongoDB.MongoEventStore.Create<EventDocument, TPayload>(
            MongoDatabase,
            MongoEventStoreOptions);
    }
}