using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public readonly struct PrefetchQueryResult(List<BsonValue> existingCommitIds, List<BsonDocument> headStreamVersions)
{
    public List<BsonValue> ExistingCommitIds { get; } = existingCommitIds;
    public List<BsonDocument> HeadStreamVersions { get; } = headStreamVersions;
}