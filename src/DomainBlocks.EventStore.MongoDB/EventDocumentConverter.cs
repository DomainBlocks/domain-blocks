using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.MongoDB;

public sealed class EventDocumentConverter : IEventDocumentConverter<EventDocument>
{
    public EventDocument ToEventDocument<TPayload>(
        string streamId,
        StreamVersion streamVersion,
        UncommittedEvent<TPayload> @event,
        DateTime committedAt) where TPayload : notnull
    {
        return new EventDocument
        {
            StreamId = streamId,
            StreamVersion = streamVersion.ToInt64(),
            EventName = @event.Header.EventName,
            Metadata = @event.Header.Metadata.ToDictionary(),
            CommittedAt = committedAt,
            Payload = BsonPayloadConverter.ToBsonValue(@event.Payload)
        };
    }

    public CommittedEvent<TPayload> FromEventDocument<TPayload>(EventDocument document) where TPayload : notnull
    {
        return CommittedEvent.Create(
            new CommittedEventHeader(
                document.StreamId,
                StreamVersion.FromInt64(document.StreamVersion),
                document.EventName,
                document.Metadata,
                document.CommittedAt),
            BsonPayloadConverter.FromBsonValue<TPayload>(document.Payload));
    }
}