using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB.Coordination;

internal interface ICommitObserver
{
    void OnCommitPositionAdvanced(long commitPosition);

    void OnCommitted(Guid commitId);

    void OnConflictRejected(Guid commitId, BsonValue conflict);
}