using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.MongoDB;

public interface IEventDocumentCodec<TEvent, TEventDocument> where TEvent : notnull
{
    IEventDocumentEncoder<TEvent, TEventDocument> CreateEncoder();

    ReadEvent<TEvent> Decode(TEventDocument document);
}