namespace DomainBlocks.EventStore.PostgreSQL.Feeds;

/// <summary>
/// The exception that is thrown when a feed's replication slot has been dropped or invalidated.
/// </summary>
internal sealed class ReplicationSlotLostException(string slotName, Exception innerException) :
    Exception($"Replication slot '{slotName}' was dropped or invalidated.", innerException);