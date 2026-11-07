namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// The exception that is thrown when a commit completes without confirmation from the synchronous standbys.
/// </summary>
internal sealed class CommitNotConfirmedException(string message) : Exception(message);