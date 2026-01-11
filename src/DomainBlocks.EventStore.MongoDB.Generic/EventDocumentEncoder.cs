using DomainBlocks.EventStore.Abstractions;
using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB.Generic;

public sealed class EventDocumentEncoder<TEvent>(
    IEventEncoder<TEvent, BsonValue, BsonValue> eventEncoder) :
    IEventDocumentEncoder<TEvent, EventDocument>
    where TEvent : notnull
{
    public EventDocument Encode(
        AppendEvent<TEvent> @event,
        string streamId,
        StreamVersion streamVersion,
        DateTime createdAtUtc)
    {
        var (eventName, eventData, metadata) = eventEncoder.Encode(@event);

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