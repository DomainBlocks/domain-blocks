using DomainBlocks.Experimental.Persistence.Serialization;

namespace DomainBlocks.Experimental.Persistence.Adapters;

public interface IWriteEventAdapter<out TWriteEvent>
{
    TWriteEvent CreateWriteEvent(
        string eventName, object payload, Dictionary<string, string>? metadata, IEventDataSerializer serializer);
}