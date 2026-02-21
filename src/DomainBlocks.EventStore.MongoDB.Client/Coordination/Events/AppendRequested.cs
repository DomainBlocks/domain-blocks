namespace DomainBlocks.EventStore.MongoDB.Client.Coordination.Events;

public sealed record AppendRequested(Guid CommitId) : IChangeEvent;