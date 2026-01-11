using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.MongoDB.Strict;

public interface IEventDocumentEncoder<TEvent, out TEventDocument> where TEvent : notnull
{
    TEventDocument Encode(
        AppendEvent<TEvent> @event,
        string streamId,
        StreamVersion streamVersion,
        Guid commitId,
        DateTime createdAtUtc);
}