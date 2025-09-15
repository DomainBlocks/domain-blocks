using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.MongoDB;

public sealed class EventDocumentMapper<TPayload> : IEventDocumentMapper<EventDocument<TPayload>, TPayload>
{
    public EventDocument<TPayload> ToEventDocument(
        string streamId,
        StreamVersion streamVersion,
        UncommittedEvent<TPayload> @event,
        DateTime committedAt)
    {
        return new EventDocument<TPayload>
        {
            StreamId = streamId,
            StreamVersion = streamVersion.ToInt64(),
            EventName = @event.Header.EventName,
            Metadata = @event.Header.Metadata.ToDictionary(),
            CommittedAt = committedAt,
            Payload = @event.Payload
        };
    }

    public CommittedEvent<TPayload> FromEventDocument(EventDocument<TPayload> document)
    {
        return CommittedEvent.Create(
            new CommittedEventHeader(
                document.StreamId,
                StreamVersion.FromInt64(document.StreamVersion),
                document.EventName,
                document.Metadata,
                document.CommittedAt),
            document.Payload);
    }
}