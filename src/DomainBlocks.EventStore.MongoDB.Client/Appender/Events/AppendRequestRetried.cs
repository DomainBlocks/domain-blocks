using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB.Client.Appender.Events;

public sealed record AppendRequestRetried(Guid CommitId, BsonDateTime LastSeenAtUtc) : IAppenderEvent;