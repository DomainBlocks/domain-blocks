using DomainBlocks.EventStore.Abstractions.Events;
using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB;

/// <summary>
/// Provides conversion between event wrappers and the default event document for Mongo persistence.
/// </summary>
public sealed class EventDocumentConverter : IEventDocumentConverter<EventDocument, BsonValue, BsonValue>
{
    /// <inheritdoc/>
    public EventDocument ToEventDocument(
        AppendEvent<BsonValue, BsonValue> @event,
        string streamId,
        StreamVersion streamVersion,
        DateTime createdAt)
    {
        return new EventDocument
        {
            StreamId = streamId,
            StreamVersion = streamVersion.Value,
            EventName = @event.EventName,
            CreatedAt = createdAt,
            EventData = @event.EventData,
            Metadata = @event.Metadata ?? BsonNull.Value
        };
    }

    /// <inheritdoc/>
    public ReadEvent<BsonValue, BsonValue> FromEventDocument(EventDocument document)
    {
        var context = new ReadEventContext(
            document.StreamId,
            new StreamVersion(document.StreamVersion),
            document.CreatedAt,
            null);

        return ReadEvent.Create(document.EventName, document.EventData, document.Metadata, context);
    }
}