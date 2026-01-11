using DomainBlocks.EventStore.Abstractions;
using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB.Strict;

public sealed class EventDocumentEncoder<TEvent>(
    IEventEncoder<TEvent, BsonValue, BsonValue> eventEncoder) :
    IEventDocumentEncoder<TEvent, EventDocument>
    where TEvent : notnull
{
    public EventDocument Encode(
        AppendEvent<TEvent> @event,
        string streamId,
        StreamVersion streamVersion,
        Guid commitId,
        DateTime createdAtUtc)
    {
        var (eventName, eventData, metadata) = eventEncoder.Encode(@event);

        var streamVersionValue = checked((long)streamVersion.Value);

        return new EventDocument
        {
            StreamId = streamId,
            StreamVersion = streamVersionValue,
            CommitId = commitId,
            EventName = eventName,
            CreatedAtUtc = createdAtUtc,
            EventData = eventData,
            Metadata = metadata ?? BsonNull.Value
        };
    }
}