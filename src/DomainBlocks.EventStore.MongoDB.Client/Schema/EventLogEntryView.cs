using MongoDB.Bson;
using static DomainBlocks.EventStore.MongoDB.Client.Schema.EventLogEntry;

namespace DomainBlocks.EventStore.MongoDB.Client.Schema;

internal readonly struct EventLogEntryView(BsonDocument doc)
{
    public long Position => doc["_id"].AsInt64;
    public long Epoch => doc[FieldNames.Epoch].AsInt64;
    public string StreamId => doc[FieldNames.StreamId].AsString;
    public long StreamVersion => doc[FieldNames.StreamVersion].AsInt64;
    public Guid CommitId => doc[FieldNames.CommitId].AsGuid;
    public string EventName => doc[FieldNames.EventName].AsString;
    public BsonValue EventData => doc[FieldNames.EventData];
    public BsonValue Metadata => doc[FieldNames.Metadata];
    public DateTime WrittenAtUtc => doc[FieldNames.WrittenAtUtc].ToUniversalTime();
}