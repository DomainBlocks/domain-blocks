using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.MongoDB.Strict;

public interface IEventDocumentEncoder<TEvent> where TEvent : notnull
{
    EventDocument Encode(
        AppendEvent<TEvent> @event,
        string streamId,
        StreamVersion streamVersion,
        Guid commitId,
        DateTime createdAtUtc);
}