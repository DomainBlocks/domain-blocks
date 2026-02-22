namespace DomainBlocks.EventStore.MongoDB.Client.Coordination.ChangeEvents;

public sealed record CommitPositionAdvanced(long CommitPosition) : IChangeEvent;