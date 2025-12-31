using DomainBlocks.Serialization.Abstractions;
using MongoDB.Bson;

namespace DomainBlocks.Serialization.MongoDB.Bson;

public sealed class BsonDocumentMetadataSerializer : IMetadataSerializer<BsonDocument>, IMetadataSerializer<BsonValue>
{
    public BsonDocument Serialize(IReadOnlyDictionary<string, string> metadata)
    {
        var doc = new BsonDocument();

        foreach (var (key, value) in metadata)
            doc.Add(key, value);

        return doc;
    }

    public IReadOnlyDictionary<string, string> Deserialize(BsonDocument metadata)
    {
        return metadata.ToDictionary(x => x.Name, x => x.Value.AsString);
    }

    BsonValue IMetadataSerializer<BsonValue>.Serialize(IReadOnlyDictionary<string, string> metadata) =>
        Serialize(metadata);

    public IReadOnlyDictionary<string, string> Deserialize(BsonValue metadata) => Deserialize(metadata.AsBsonDocument);
}