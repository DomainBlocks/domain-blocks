using DomainBlocks.Core.Exceptions;

namespace DomainBlocks.EventStore.Exceptions;

public class EventTypeMappingNotFoundException(string? message = null, Exception? innerException = null) :
    DomainBlocksException(message, innerException);