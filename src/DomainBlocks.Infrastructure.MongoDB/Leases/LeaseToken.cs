namespace DomainBlocks.Infrastructure.MongoDB.Leases;

public sealed record LeaseToken(string ResourceId, string HolderId, long Epoch);