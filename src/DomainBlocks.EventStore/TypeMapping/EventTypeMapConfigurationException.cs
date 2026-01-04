using DomainBlocks.Core.Exceptions;

namespace DomainBlocks.EventStore.TypeMapping;

public sealed class EventTypeMapConfigurationException(string message) : DomainBlocksException(message);