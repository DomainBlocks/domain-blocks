using MongoDB.Bson;
using static DomainBlocks.EventStore.MongoDB.Client.Schema.PendingEvent;

namespace DomainBlocks.EventStore.MongoDB.Client.Schema;

public readonly struct PendingEventView(BsonDocument doc)
{
    public string EventName => doc[FieldNames.EventName].AsString;
    public BsonValue EventData => doc[FieldNames.EventData];
    public BsonValue Metadata => doc[FieldNames.Metadata];
}