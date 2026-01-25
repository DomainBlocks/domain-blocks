using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.MongoDB.Generic;

public interface IEventDocumentCodec<TEvent, TEventDocument> where TEvent : notnull
{
    IEnumerable<TEventDocument> Encode(
        IEnumerable<AppendEvent<TEvent>> events,
        string streamId,
        StreamVersion? currentStreamVersion);

    ReadEvent<TEvent> Decode(TEventDocument document);
}