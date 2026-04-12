using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DomainBlocks.EventStore.MongoDB.Schema;

// ReSharper disable all
internal sealed class PendingEvent
{
    [BsonElement(FieldNames.EventName)]
    public required string EventName { get; init; }

    [BsonElement(FieldNames.EventData)]
    public required BsonValue EventData { get; init; }

    [BsonElement(FieldNames.Metadata)]
    public required BsonValue Metadata { get; init; }

    public static class FieldNames
    {
        public const string EventName = "eventName";
        public const string EventData = "eventData";
        public const string Metadata = "metadata";
    }
}