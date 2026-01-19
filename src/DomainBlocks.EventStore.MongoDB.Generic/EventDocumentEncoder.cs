using DomainBlocks.EventStore.Abstractions;
using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB.Generic;

public sealed class EventDocumentEncoder<TEvent>(
    IEventEncodingSession<TEvent, BsonValue, BsonValue> encodingSession) :
    IEventDocumentEncoder<TEvent, EventDocument>
    where TEvent : notnull
{
    public EventDocument Encode(
        AppendEvent<TEvent> @event,
        string streamId,
        StreamVersion streamVersion,
        DateTime createdAtUtc)
    {
        var (eventName, eventData, metadata) = encodingSession.Encode(@event);

        return new EventDocument
        {
            StreamId = streamId,
            StreamVersion = streamVersion.ToInt64(),
            EventName = eventName,
            CreatedAtUtc = createdAtUtc,
            EventData = eventData,
            Metadata = metadata ?? BsonNull.Value
        };
    }
}