namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// The exception that is thrown when a replication barrier completes without confirmation from the synchronous
/// standbys.
/// </summary>
internal sealed class ReplicationBarrierException(string message) : Exception(message);