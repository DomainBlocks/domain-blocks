using DomainBlocks.Experimental.Persistence.Adapters;
using DomainBlocks.Experimental.Persistence.Serialization;
using DomainBlocks.ThirdParty.SqlStreamStore.Streams;

namespace DomainBlocks.Experimental.Persistence.SqlStreamStore;

public sealed class SqlStreamStoreEventAdapter : IEventAdapter<StreamMessage, NewStreamMessage>
{
    public string GetEventName(StreamMessage readEvent) => readEvent.Type;

    public async ValueTask<object> Deserialize(StreamMessage readEvent, Type type, IEventDataSerializer serializer)
    {
        var data = await readEvent.GetJsonData();
        return serializer.Deserialize(data, type);
    }

    public Dictionary<string, string> DeserializeMetadata(StreamMessage readEvent, IEventDataSerializer serializer)
    {
        if (string.IsNullOrEmpty(readEvent.JsonMetadata))
        {
            return new Dictionary<string, string>();
        }

        return (Dictionary<string, string>)serializer.Deserialize(
            readEvent.JsonMetadata, typeof(Dictionary<string, string>));
    }

    public StreamVersion GetStreamVersion(StreamMessage readEvent) =>
        StreamVersion.FromUInt64(Convert.ToUInt64(readEvent.StreamVersion));

    public NewStreamMessage CreateWriteEvent(
        string eventName, object payload, Dictionary<string, string>? metadata, IEventDataSerializer serializer)
    {
        var serializedData = serializer.SerializeToString(payload);
        var serializedMetadata = metadata == null ? null : serializer.SerializeToString(metadata);
        return new NewStreamMessage(Guid.NewGuid(), eventName, serializedData, serializedMetadata);
    }
}