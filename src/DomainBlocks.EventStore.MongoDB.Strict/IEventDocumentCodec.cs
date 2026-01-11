using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.MongoDB.Strict;

public interface IEventDocumentCodec<TEvent> where TEvent : notnull
{
    IEventDocumentEncoder<TEvent> CreateEncoder();

    ReadEvent<TEvent> Decode(EventDocument document);
}