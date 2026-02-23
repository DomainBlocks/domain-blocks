using DomainBlocks.Infrastructure.MongoDB.Leases;

namespace DomainBlocks.EventStore.MongoDB.Client.Cluster.Events.Local;

public sealed record LeaseLocallyAcquired(LeaseClaim Claim) : ILocalEvent;