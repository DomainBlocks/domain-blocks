using DomainBlocks.EventStore.Abstractions;
using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB.Generic;

public static class EventDocumentCodec
{
    public static EventDocumentCodec<TEvent> Create<TEvent>(
        EventCodec<TEvent, BsonValue, BsonValue> eventCodec)
        where TEvent : notnull
    {
        return new EventDocumentCodec<TEvent>(eventCodec);
    }
}

public sealed class EventDocumentCodec<TEvent>(
    EventCodec<TEvent, BsonValue, BsonValue> eventCodec) :
    IEventDocumentCodec<TEvent, EventDocument>
    where TEvent : notnull
{
    public IEnumerable<EventDocument> Encode(
        IEnumerable<AppendEvent<TEvent>> events,
        string streamId,
        StreamVersion? currentStreamVersion)
    {
        var nextVersionValue = (currentStreamVersion?.Value + 1) ?? 0;
        var createdAtUtc = DateTime.UtcNow;

        foreach (var (eventName, eventData, metadata) in eventCodec.Encoder.Encode(events))
        {
            var streamVersion = new StreamVersion(nextVersionValue++);

            yield return new EventDocument
            {
                StreamId = streamId,
                StreamVersion = streamVersion.ToInt64(),
                EventName = eventName,
                CreatedAtUtc = createdAtUtc,
                EventData = eventData,
                Metadata = metadata ?? BsonNull.Value
            };
        }
    }

    public ReadEvent<TEvent> Decode(EventDocument document)
    {
        var (@event, metadata) = eventCodec.Decoder.Decode(document.EventName, document.EventData, document.Metadata);

        var streamVersion = StreamVersion.FromInt64(document.StreamVersion);
        var context = new ReadEventContext(document.StreamId, streamVersion, document.CreatedAtUtc);

        return ReadEvent.Create(@event, metadata, context);
    }
}