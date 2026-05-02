namespace DomainBlocks.EventStore.MongoDB.Schema;

internal enum LeaseUpdateKind
{
    Acquired,
    Renewed,
    Released,
    CommitPositionAdvanced
}