using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.MongoDB;

public interface IEventDocumentMapper<TEventDocument, TPayload>
{
    TEventDocument ToEventDocument(
        string streamId,
        StreamVersion streamVersion,
        UncommittedEvent<TPayload> @event,
        DateTime committedAt);

    CommittedEvent<TPayload> FromEventDocument(TEventDocument document);
}