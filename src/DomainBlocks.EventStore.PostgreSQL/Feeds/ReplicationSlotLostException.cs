namespace DomainBlocks.EventStore.PostgreSQL.Feeds;

/// <summary>
/// The feed's replication slot was dropped or invalidated, so the feed continues on a new slot, and resets.
/// </summary>
internal sealed class ReplicationSlotLostException(string slotName, Exception innerException) :
    Exception($"Replication slot '{slotName}' was dropped or invalidated.", innerException);