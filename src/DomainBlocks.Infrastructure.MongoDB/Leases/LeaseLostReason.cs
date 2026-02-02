namespace DomainBlocks.Infrastructure.MongoDB.Leases;

public enum LeaseLostReason
{
    Released,
    Revoked,
    Error
}