using DomainBlocks.Infrastructure.MongoDB.Leases;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination.ChangeEvents;

public sealed record LeaseRenewed(LeaseClaim Claim) : IChangeEvent;