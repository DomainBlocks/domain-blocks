namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// The exception that is thrown when a commit completes without confirmation from the synchronous standbys.
/// </summary>
/// <remarks>
/// The commit stands on the primary, but a failover can lose it.
/// </remarks>
internal sealed class CommitNotConfirmedException(string message) : Exception(message);