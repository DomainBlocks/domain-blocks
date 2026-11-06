namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// The exception that is thrown when a commit, such as an append batch's or a replication barrier's, completes without
/// confirmation from the synchronous standbys.
/// </summary>
/// <remarks>
/// The commit stands on the primary, but the standbys might not have it, so a failover can lose it.
/// </remarks>
internal sealed class CommitNotConfirmedException(string message) : Exception(message);