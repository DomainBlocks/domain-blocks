namespace DomainBlocks.EventStore.MongoDB.Coordination;

public enum LeaseLostReason
{
    Released,
    Revoked,
    Error
}