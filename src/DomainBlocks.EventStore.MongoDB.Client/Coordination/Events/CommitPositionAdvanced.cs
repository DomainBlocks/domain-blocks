namespace DomainBlocks.EventStore.MongoDB.Client.Coordination.Events;

public sealed record CommitPositionAdvanced(long CommitPosition) : IChangeEvent;