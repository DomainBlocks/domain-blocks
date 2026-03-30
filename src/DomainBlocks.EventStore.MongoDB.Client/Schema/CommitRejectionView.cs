using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Client.Serialization;
using MongoDB.Bson;
using static DomainBlocks.EventStore.MongoDB.Client.Schema.CommitRejection;

namespace DomainBlocks.EventStore.MongoDB.Client.Schema;

public readonly struct CommitRejectionView(BsonDocument doc)
{
    public Guid CommitId => doc[FieldNames.CommitId].AsGuid;
    public string StreamId => doc[FieldNames.StreamId].AsString;
    public ExpectedStreamState ExpectedStreamState => doc[FieldNames.ExpectedStreamState].ToExpectedStreamState();
    public StreamState ActualStreamState => doc[FieldNames.ActualStreamState].ToStreamState();
}