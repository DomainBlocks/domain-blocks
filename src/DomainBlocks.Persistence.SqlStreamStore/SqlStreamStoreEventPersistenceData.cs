using System;
using System.Net.Mime;
using System.Threading.Tasks;
using DomainBlocks.Serialization;
using SqlStreamStore.Streams;

namespace DomainBlocks.Persistence.SqlStreamStore;

public class SqlStreamStoreEventPersistenceData : IEventPersistenceData<string>
{
    private SqlStreamStoreEventPersistenceData(Guid eventId,
        string eventName,
        string eventData,
        string eventMetadata)
    {
        EventId = eventId;
        EventName = eventName;
        ContentType = MediaTypeNames.Application.Json;
        EventData = eventData;
        EventMetadata = eventMetadata;
    }

    public Guid EventId { get; }
    public string EventName { get; }
    public string ContentType { get; }
    public string EventData { get; }
    public string EventMetadata { get; }

    public static async Task<IEventPersistenceData<string>> FromStreamMessage(StreamMessage streamMessage)
    {
        return new SqlStreamStoreEventPersistenceData(streamMessage.MessageId,
            streamMessage.Type,
            await streamMessage.GetJsonData(),
            streamMessage.JsonMetadata);
    }
}