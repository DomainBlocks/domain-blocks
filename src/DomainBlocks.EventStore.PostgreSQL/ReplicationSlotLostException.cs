using DomainBlocks.Core.Exceptions;

namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// The exception that is thrown when a feed's replication slot has been dropped or invalidated.
/// </summary>
public sealed class ReplicationSlotLostException(string slotName, Exception innerException) :
    DomainBlocksException($"Replication slot '{slotName}' was dropped or invalidated.", innerException)
{
    public string SlotName { get; } = slotName;
}