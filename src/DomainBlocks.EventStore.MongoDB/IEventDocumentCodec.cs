using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.MongoDB;

public interface IEventDocumentCodec<TEventBase, TEventDocument> where TEventBase : class
{
    IEventDocumentEncoder<TEventBase, TEventDocument> CreateEncoder();

    ReadEvent<TEventBase> FromEventDocument(TEventDocument document);
}