using DomainBlocks.Experimental.EventSourcing.Persistence;
using DomainBlocks.Experimental.EventSourcing.Persistence.Adapters;
using DomainBlocks.ThirdParty.SqlStreamStore.Streams;

namespace DomainBlocks.SqlStreamStore.Experimental;

public sealed class SqlStreamStoreEventAdapter : IEventAdapter<StreamMessage, NewStreamMessage, int, string>
{
    public string GetEventName(StreamMessage readEvent) => readEvent.Type;
    public Task<string> GetEventData(StreamMessage readEvent) => readEvent.GetJsonData();

    public bool TryGetMetadata(StreamMessage readEvent, out string? metadata)
    {
        if (string.IsNullOrEmpty(readEvent.JsonMetadata))
        {
            metadata = null;
            return false;
        }

        metadata = readEvent.JsonMetadata;
        return true;
    }

    public int GetStreamVersion(StreamMessage readEvent) => readEvent.StreamVersion;

    public NewStreamMessage CreateWriteEvent(
        string eventName, string data, string? metadata = null, string? contentType = null)
    {
        return new NewStreamMessage(Guid.NewGuid(), eventName, data, metadata);
    }
}