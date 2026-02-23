namespace DomainBlocks.EventStore.MongoDB.Client.Cluster.Events.ChangeStream;

public sealed record CommitPositionAdvanced(long CommitPosition) : IChangeStreamEvent;