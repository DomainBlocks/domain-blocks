using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination.ChangeEvents;

public sealed record AppendRequested(Guid CommitId, BsonDocument Request) : IChangeEvent;