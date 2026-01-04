using DomainBlocks.Core.Exceptions;

namespace DomainBlocks.EventStore.TypeMapping;

public class EventTypeMappingNotFoundException(string? message = null, Exception? innerException = null) :
    DomainBlocksException(message, innerException);