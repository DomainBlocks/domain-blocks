using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination.ChangeEvents;

public sealed record AppendRequestRetried(Guid CommitId, BsonDateTime LastSeenAtUtc) : IChangeEvent;