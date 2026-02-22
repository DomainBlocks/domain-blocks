using DomainBlocks.Infrastructure.MongoDB.Leases;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination.ChangeEvents;

public sealed record LeaseReleased(LeaseClaim Claim) : IChangeEvent;