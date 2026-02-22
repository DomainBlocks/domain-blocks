namespace DomainBlocks.Infrastructure.MongoDB.Leases;

public enum LeaseUpdateKind
{
    Unknown = 0,
    Acquired,
    Renewed,
    Released,
    StateUpdated
}