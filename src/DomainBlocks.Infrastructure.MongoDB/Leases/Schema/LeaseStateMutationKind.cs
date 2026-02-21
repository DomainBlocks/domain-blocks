namespace DomainBlocks.Infrastructure.MongoDB.Leases.Schema;

public enum LeaseStateMutationKind
{
    Unknown = 0,
    Acquire,
    Renew,
    IncrementCounter,
    Release
}