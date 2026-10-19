namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// The replication barrier was not confirmed, so what it followed must not be used yet. The read is retried.
/// </summary>
internal sealed class ReplicationBarrierException(string message) : Exception(message);