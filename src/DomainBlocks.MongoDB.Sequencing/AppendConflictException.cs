using DomainBlocks.Core.Exceptions;

namespace DomainBlocks.MongoDB.Sequencing;

public class AppendConflictException(string? message = null, Exception? innerException = null) :
    DomainBlocksException(message, innerException);