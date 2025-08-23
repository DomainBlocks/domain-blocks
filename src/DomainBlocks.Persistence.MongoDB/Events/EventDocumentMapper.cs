using System.Collections.Frozen;
using DomainBlocks.Persistence.Abstractions.Events;

namespace DomainBlocks.Persistence.MongoDB.Events;

public sealed class EventDocumentMapper<TPayload> : IEventDocumentMapper<EventDocument<TPayload>, TPayload>
{
    public EventDocument<TPayload> ToEventDocument(
        string streamId,
        long streamVersion,
        NewEventRecord<TPayload> @event,
        DateTime committedAt)
    {
        return new EventDocument<TPayload>
        {
            StreamId = streamId,
            StreamVersion = streamVersion,
            EventName = @event.Header.EventName,
            Metadata = @event.Header.Metadata,
            CommittedAt = committedAt,
            Payload = @event.Payload
        };
    }

    public EventRecord<TPayload> FromEventDocument(EventDocument<TPayload> document)
    {
        return new EventRecord<TPayload>(
            new EventHeader(
                document.StreamId,
                StreamVersion.FromInt64(document.StreamVersion),
                document.EventName,
                document.Metadata.ToFrozenDictionary(),
                document.CommittedAt),
            document.Payload);
    }
}