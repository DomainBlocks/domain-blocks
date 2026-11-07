using DomainBlocks.Core.Exceptions;

namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// The exception that is thrown when a feed can't resume on its replication slot because another process holds it.
/// </summary>
public sealed class ReplicationSlotInUseException(string slotName) :
    DomainBlocksException($"Replication slot '{slotName}' is in use by another process.")
{
    public string SlotName { get; } = slotName;
}