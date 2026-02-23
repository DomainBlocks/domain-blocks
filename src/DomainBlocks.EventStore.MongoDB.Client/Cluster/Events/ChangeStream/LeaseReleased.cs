using DomainBlocks.Infrastructure.MongoDB.Leases;

namespace DomainBlocks.EventStore.MongoDB.Client.Cluster.Events.ChangeStream;

public sealed record LeaseReleased(LeaseClaim Claim) : IChangeStreamEvent;