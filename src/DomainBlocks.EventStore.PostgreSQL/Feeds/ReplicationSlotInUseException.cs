namespace DomainBlocks.EventStore.PostgreSQL.Feeds;

/// <summary>
/// The exception that is thrown when a feed can't resume on its replication slot because another process holds it.
/// </summary>
internal sealed class ReplicationSlotInUseException(string slotName, int processId) :
    Exception($"Replication slot '{slotName}' is in use by process {processId}.");