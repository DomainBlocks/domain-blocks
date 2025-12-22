using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace DomainBlocks.Testing.Integration.MongoDB;

public abstract class MongoEventStoreTestFixture
{
    static MongoEventStoreTestFixture()
    {
        BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
    }
}