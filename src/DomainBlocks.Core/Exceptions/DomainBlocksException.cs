namespace DomainBlocks.Core.Exceptions;

/// <summary>
/// Serves as the base class for the exception types that DomainBlocks defines.
/// </summary>
public class DomainBlocksException(string? message = null, Exception? innerException = null) :
    Exception(message, innerException);