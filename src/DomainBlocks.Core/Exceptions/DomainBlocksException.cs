namespace DomainBlocks.Core.Exceptions;

/// <summary>
/// Base type for exceptions thrown by the DomainBlocks library.
/// </summary>
public class DomainBlocksException(string? message = null, Exception? innerException = null) :
    Exception(message, innerException);