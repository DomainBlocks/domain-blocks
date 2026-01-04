using DomainBlocks.EventStore.Abstractions;
using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB;

public static class EventDocumentCodec
{
    public static EventDocumentCodec<TEventBase> Create<TEventBase>(
        IEventCodec<TEventBase, BsonValue, BsonValue> eventCodec)
        where TEventBase : class
    {
        return new EventDocumentCodec<TEventBase>(eventCodec);
    }
}

public sealed class EventDocumentCodec<TEventBase>(IEventCodec<TEventBase, BsonValue, BsonValue> eventCodec) :
    IEventDocumentCodec<TEventBase, EventDocument>
    where TEventBase : class
{
    public IEventDocumentEncoder<TEventBase, EventDocument> CreateEncoder()
    {
        return new EventDocumentEncoder<TEventBase>(eventCodec.CreateEncoder());
    }

    public ReadEvent<TEventBase> FromEventDocument(EventDocument document)
    {
        var context = new ReadEventContext(
            document.StreamId,
            new StreamVersion(document.StreamVersion),
            document.CreatedAtUtc,
            null);

        return eventCodec.Decode(document.EventName, document.EventData, document.Metadata, context);
    }
}