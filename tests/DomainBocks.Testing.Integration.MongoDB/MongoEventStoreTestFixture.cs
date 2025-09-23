using DomainBlocks.EventStore.MongoDB;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using NUnit.Framework;

namespace DomainBocks.Testing.Integration.MongoDB;

public class MongoEventStoreTestFixture<TPayload> where TPayload : notnull
{
    static MongoEventStoreTestFixture()
    {
        BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
    }

    protected IMongoEventStore<TPayload> MongoEventStore { get; private set; } = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        var client = new MongoClient("mongodb://localhost:27017");
        var database = client.GetDatabase("test");
        var options = MongoEventStoreOptions.CreateDefault<TPayload>();
        await MongoEventStoreAdmin.EnsureIndexesAsync(database, options);

        MongoEventStore = DomainBlocks.EventStore.MongoDB.MongoEventStore.Create(database, options);
    }
}