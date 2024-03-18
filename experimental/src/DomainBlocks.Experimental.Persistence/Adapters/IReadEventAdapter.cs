using DomainBlocks.Experimental.Persistence.Serialization;

namespace DomainBlocks.Experimental.Persistence.Adapters;

public interface IReadEventAdapter<in TReadEvent>
{
    string GetEventName(TReadEvent readEvent);
    ValueTask<object> Deserialize(TReadEvent readEvent, Type type, IEventDataSerializer serializer);
    Dictionary<string, string> DeserializeMetadata(TReadEvent readEvent, IEventDataSerializer serializer);
    StreamVersion GetStreamVersion(TReadEvent readEvent);
}