using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB.Coordination;

public interface ICommitObserver
{
    void OnCommitted(Guid commitId);

    void OnConflictRejected(Guid commitId, BsonValue conflict);
}