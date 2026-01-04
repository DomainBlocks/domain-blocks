using DomainBlocks.EventStore.Abstractions;
using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB;

public static class EventDocumentCodec
{
    public static EventDocumentCodec<TEvent> Create<TEvent>(
        IEventCodec<TEvent, BsonValue, BsonValue> eventCodec)
        where TEvent : notnull
    {
        return new EventDocumentCodec<TEvent>(eventCodec);
    }
}

public sealed class EventDocumentCodec<TEvent>(
    IEventCodec<TEvent, BsonValue, BsonValue> eventCodec) :
    IEventDocumentCodec<TEvent, EventDocument>
    where TEvent : notnull
{
    public IEventDocumentEncoder<TEvent, EventDocument> CreateEncoder()
    {
        return new EventDocumentEncoder<TEvent>(eventCodec.CreateEncoder());
    }

    public ReadEvent<TEvent> Decode(EventDocument document)
    {
        var (@event, metadata) = eventCodec.Decode(document.EventName, document.EventData, document.Metadata);

        var context = new ReadEventContext(
            document.StreamId,
            new StreamVersion(document.StreamVersion),
            document.CreatedAtUtc);

        return ReadEvent.Create(@event, metadata, context);
    }
}