using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Client.Serialization;
using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB.Client.Schema;

public readonly struct AppendRequestView(BsonDocument doc)
{
    public ObjectId Id => doc["_id"].AsObjectId;
    public Guid CommitId => doc[FieldNames.CommitId].AsGuid;
    public string StreamId => doc[FieldNames.StreamId].AsString;

    public ExpectedStreamState ExpectedStreamState =>
        doc[FieldNames.ExpectedStreamState].AsBsonDocument.ToExpectedStreamState();

    public IEnumerable<PendingEventView> Events =>
        doc[FieldNames.Events].AsBsonArray.Select(x => new PendingEventView(x.AsBsonDocument));

    public DateTime CreatedAtUtc => doc[FieldNames.CreatedAtUtc].ToUniversalTime();
}