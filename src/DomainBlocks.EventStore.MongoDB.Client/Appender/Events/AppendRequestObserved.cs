using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB.Client.Appender.Events;

public sealed record AppendRequestObserved(Guid CommitId, BsonDocument Request) : IAppenderEvent;