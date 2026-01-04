using DomainBlocks.EventStore.Abstractions;
using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB;

public sealed class EventDocumentEncoder<TEventBase>(IEventEncoder<TEventBase, BsonValue, BsonValue> eventEncoder) :
    IEventDocumentEncoder<TEventBase, EventDocument>
    where TEventBase : class
{
    public EventDocument Encode(
        AppendEvent<TEventBase> @event,
        string streamId,
        StreamVersion streamVersion,
        DateTime createdAtUtc)
    {
        var (eventName, eventData, metadata) = eventEncoder.Encode(@event);

        return new EventDocument
        {
            StreamId = streamId,
            StreamVersion = streamVersion.Value,
            EventName = eventName,
            CreatedAtUtc = createdAtUtc,
            EventData = eventData,
            Metadata = metadata ?? BsonNull.Value
        };
    }
}