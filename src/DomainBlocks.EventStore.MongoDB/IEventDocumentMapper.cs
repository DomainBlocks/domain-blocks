using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.MongoDB;

public interface IEventDocumentMapper<TEventDocument, TPayload>
{
    TEventDocument ToEventDocument(
        string streamId,
        StreamVersion streamVersion,
        NewEventRecord<TPayload> @event,
        DateTime committedAt);

    EventRecord<TPayload> FromEventDocument(TEventDocument document);
}