using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB.Client.Cluster.Events.ChangeStream;

public sealed record AppendRequested(Guid CommitId, BsonDocument Request) : IChangeStreamEvent;