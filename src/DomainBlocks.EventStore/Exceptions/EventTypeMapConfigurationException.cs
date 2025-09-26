using DomainBlocks.Core.Exceptions;

namespace DomainBlocks.EventStore.Exceptions;

public sealed class EventTypeMapConfigurationException(string message) : DomainBlocksException(message);