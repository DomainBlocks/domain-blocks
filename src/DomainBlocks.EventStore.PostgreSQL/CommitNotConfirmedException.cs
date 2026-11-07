using DomainBlocks.Core.Exceptions;

namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// The exception that is thrown when a commit completes without confirmation from the synchronous standbys.
/// </summary>
public sealed class CommitNotConfirmedException(string message) : DomainBlocksException(message);