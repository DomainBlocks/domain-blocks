namespace DomainBlocks.EventStore.MongoDB.Coordination;

internal enum LeaseLostReason
{
    Released,
    Revoked,
    Error
}