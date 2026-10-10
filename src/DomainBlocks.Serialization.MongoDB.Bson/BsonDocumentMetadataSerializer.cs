using System.Collections.Frozen;
using DomainBlocks.Serialization.Abstractions;
using MongoDB.Bson;

namespace DomainBlocks.Serialization.MongoDB.Bson;

public sealed class BsonDocumentMetadataSerializer : IMetadataSerializer<BsonDocument>, IMetadataSerializer<BsonValue>
{
    public BsonDocument Serialize(ReadOnlySpan<KeyValuePair<string, string>> metadata)
    {
        var doc = new BsonDocument();

        foreach (var (key, value) in metadata)
            doc.Add(key, value);

        return doc;
    }

    public IReadOnlyDictionary<string, string> Deserialize(BsonDocument data)
    {
        return data.ToDictionary(x => x.Name, x => x.Value.AsString);
    }

    BsonValue IMetadataSerializer<BsonValue>.Serialize(ReadOnlySpan<KeyValuePair<string, string>> metadata) =>
        Serialize(metadata);

    IReadOnlyDictionary<string, string> IMetadataSerializer<BsonValue>.Deserialize(BsonValue data)
    {
        // A read that excludes metadata leaves the field out of the document, so the store passes BsonNull here.
        return !data.IsBsonNull
            ? Deserialize(data.AsBsonDocument)
            : FrozenDictionary<string, string>.Empty;
    }
}