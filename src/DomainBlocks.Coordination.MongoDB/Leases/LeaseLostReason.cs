namespace DomainBlocks.Coordination.MongoDB.Leases;

public enum LeaseLostReason
{
    Released,
    Revoked,
    Error
}