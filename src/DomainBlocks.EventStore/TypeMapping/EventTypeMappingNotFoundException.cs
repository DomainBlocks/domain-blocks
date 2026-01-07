using DomainBlocks.Core.Exceptions;

namespace DomainBlocks.EventStore.TypeMapping;

public class EventTypeMappingNotFoundException(string message) : DomainBlocksException(message);