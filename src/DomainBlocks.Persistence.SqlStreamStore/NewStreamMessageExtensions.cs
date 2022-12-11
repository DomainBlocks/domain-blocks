using System.Collections.Generic;
using DomainBlocks.Serialization;
using SqlStreamStore.Streams;

namespace DomainBlocks.Persistence.SqlStreamStore;

public static class NewStreamMessageExtensions
{
    public static NewStreamMessage ToNewStreamMessage(
        this IEventSerializer<string> eventSerializer,
        object @event,
        string eventNameOverride = null,
        params KeyValuePair<string, string>[] additionalMetadata)
    {
        var eventPersistenceData = eventSerializer.GetPersistenceData(@event, eventNameOverride, additionalMetadata);

        return new NewStreamMessage(
            eventPersistenceData.EventId,
            eventPersistenceData.EventName,
            eventPersistenceData.EventData,
            eventPersistenceData.EventMetadata);
    }
}