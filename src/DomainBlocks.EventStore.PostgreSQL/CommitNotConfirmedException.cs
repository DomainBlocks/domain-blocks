namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// A commit's wait for its flush and for the synchronous standbys was cut short, so the commit stands on the primary
/// but may not survive a failover. What it covered must not be used or acknowledged yet. The work is retried: a read
/// with a replication barrier runs again, and an append batch runs again, which reports the requests that committed as
/// duplicates once the standbys have them.
/// </summary>
internal sealed class CommitNotConfirmedException(string message) : Exception(message);