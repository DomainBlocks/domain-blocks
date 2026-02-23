using DomainBlocks.Infrastructure.MongoDB.Leases;

namespace DomainBlocks.EventStore.MongoDB.Client.Cluster.Events.ChangeStream;

public sealed record LeaseAcquired(LeaseClaim Claim) : IChangeStreamEvent;