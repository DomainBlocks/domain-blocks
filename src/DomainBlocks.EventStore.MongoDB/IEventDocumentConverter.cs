using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.MongoDB;

public interface IEventDocumentConverter<TEventDocument>
{
    TEventDocument ToEventDocument<TPayload>(
        string streamId,
        StreamVersion streamVersion,
        UncommittedEvent<TPayload> @event,
        DateTime committedAt) where TPayload : notnull;

    CommittedEvent<TPayload> FromEventDocument<TPayload>(TEventDocument document) where TPayload : notnull;
}