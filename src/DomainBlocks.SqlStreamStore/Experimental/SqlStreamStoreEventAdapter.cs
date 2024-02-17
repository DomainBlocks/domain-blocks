using DomainBlocks.Experimental.EventSourcing.Persistence;
using DomainBlocks.ThirdParty.SqlStreamStore.Streams;

namespace DomainBlocks.SqlStreamStore.Experimental;

public sealed class SqlStreamStoreEventAdapter : IEventAdapter<StreamMessage, NewStreamMessage, string, int>
{
    public string GetEventName(StreamMessage readEvent) => readEvent.Type;
    public Task<string> GetEventData(StreamMessage readEvent) => readEvent.GetJsonData();
    public string? GetEventMetadata(StreamMessage readEvent) => readEvent.JsonMetadata;
    public int GetStreamVersion(StreamMessage readEvent) => readEvent.StreamVersion;

    public NewStreamMessage CreateWriteEvent(
        string eventName, string data, string? metadata = null, string? contentType = null)
    {
        return new NewStreamMessage(Guid.NewGuid(), eventName, data, metadata);
    }
}