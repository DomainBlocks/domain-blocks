namespace DomainBlocks.Infrastructure.MongoDB.Leases;

public sealed record LeaseClaim(string ResourceId, string HolderId, long Epoch);