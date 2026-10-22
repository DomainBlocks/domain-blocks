namespace DomainBlocks.EventStore.PostgreSQL.Feeds;

/// <summary>
/// A walsender that the feed can't end still holds the feed's replication slot, so the feed tries again later.
/// </summary>
internal sealed class ReplicationSlotInUseException(string slotName, int processId) :
    Exception($"Replication slot '{slotName}' is in use by process {processId}.");