using DomainBlocks.Persistence.Abstractions.Events;

namespace DomainBlocks.Persistence.MongoDB.Events;

public interface IEventDocumentMapper<TEventDocument, TPayload>
{
    TEventDocument ToEventDocument(
        string streamId,
        long streamVersion,
        NewEventRecord<TPayload> @event,
        DateTime committedAt);

    EventRecord<TPayload> FromEventDocument(TEventDocument document);
}