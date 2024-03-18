using DomainBlocks.Experimental.Persistence.Serialization;

namespace DomainBlocks.Experimental.Persistence.Events;

public interface IWriteEventFactory
{
    WriteEvent Create(
        string eventName,
        object payload,
        Dictionary<string, string>? metadata,
        IEventDataSerializer serializer);
}