using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public interface ICommitListener
{
    void OnCommitted(Guid commitId);

    void OnCommitRejected(Guid commitId, BsonValue rejection);
}