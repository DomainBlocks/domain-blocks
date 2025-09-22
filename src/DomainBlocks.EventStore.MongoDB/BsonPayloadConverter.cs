using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB;

public static class BsonPayloadConverter
{
    public static BsonValue ToBsonValue(object payload)
    {
        return payload switch
        {
            BsonDocument p => p,
            byte[] p => new BsonBinaryData(p),
            string p => p,
            _ => throw new NotSupportedException($"Unsupported payload type '{payload.GetType()}'.")
        };
    }

    public static TPayload FromBsonValue<TPayload>(BsonValue bsonValue)
    {
        object payload = typeof(TPayload) switch
        {
            var t when t == typeof(BsonDocument) => bsonValue.AsBsonDocument,
            var t when t == typeof(byte[]) => bsonValue.AsBsonBinaryData.Bytes,
            var t when t == typeof(string) => bsonValue.AsString,
            _ => throw new NotSupportedException($"Unsupported payload type '{typeof(TPayload)}'.")
        };

        return (TPayload)payload;
    }
}