namespace DomainBlocks.EventStore.MongoDB.Appender.Coordination;

public enum LeaseLostReason
{
    Released,
    Revoked,
    Error
}