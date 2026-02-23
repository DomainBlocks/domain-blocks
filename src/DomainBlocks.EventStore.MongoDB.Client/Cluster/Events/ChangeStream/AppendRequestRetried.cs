using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB.Client.Cluster.Events.ChangeStream;

public sealed record AppendRequestRetried(Guid CommitId, BsonDateTime LastSeenAtUtc) : IChangeStreamEvent;