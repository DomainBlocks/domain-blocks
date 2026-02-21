using DomainBlocks.Infrastructure.MongoDB.Leases;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination.Events;

public sealed record LeaseAcquired(LeaseClaim Claim) : IChangeEvent;