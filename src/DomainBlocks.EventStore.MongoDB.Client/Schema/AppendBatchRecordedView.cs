using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB.Client.Schema;

public readonly struct AppendBatchRecordedView(BsonDocument doc)
{
    public IEnumerable<Guid> AppendedCommitIds => doc[FieldNames.AppendedCommitIds].AsBsonArray.Select(x => x.AsGuid);

    public IEnumerable<Guid> DuplicateCommitIds => doc[FieldNames.DuplicateCommitIds].AsBsonArray.Select(x => x.AsGuid);

    public IEnumerable<CommitRejectionView> Rejections =>
        doc[FieldNames.Rejections].AsBsonArray.Select(x => new CommitRejectionView(x.AsBsonDocument));
}