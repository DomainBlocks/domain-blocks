using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.MongoDB.Generic;

public interface IEventDocumentEncoder<TEvent, out TEventDocument> where TEvent : notnull
{
    TEventDocument Encode(
        AppendEvent<TEvent> @event,
        string streamId,
        StreamVersion streamVersion,
        DateTime createdAtUtc);
}