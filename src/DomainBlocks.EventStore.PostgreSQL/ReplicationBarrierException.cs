namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// The exception that is thrown when the synchronous standbys don't confirm a replication barrier.
/// </summary>
internal sealed class ReplicationBarrierException(string message) : Exception(message);