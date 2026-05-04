using DomainBlocks.Core.Exceptions;

namespace DomainBlocks.MongoDB.Sequencing;

/// <summary>
/// Thrown when an append operation fails due to an unresolved conflict.
/// </summary>
public class AppendConflictException(string? message = null, Exception? innerException = null) :
    DomainBlocksException(message, innerException);