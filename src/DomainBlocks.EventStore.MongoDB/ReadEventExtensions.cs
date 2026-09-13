using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Abstractions.Codecs;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

internal static class ReadEventExtensions
{
    extension(IFindFluent<BsonDocument, BsonDocument> find)
    {
        /// <summary>
        /// Leaves the metadata field out of the documents returned when the caller does not want it.
        /// </summary>
        public IFindFluent<BsonDocument, BsonDocument> ProjectMetadata(bool includeMetadata)
        {
            return includeMetadata
                ? find
                : find.Project<BsonDocument>(
                    Builders<BsonDocument>.Projection.Exclude(EventLogEntry.FieldNames.Metadata));
        }
    }

    extension<TEvent>(IEventDecoder<TEvent, BsonValue, BsonValue> decoder) where TEvent : notnull
    {
        public ReadEvent<TEvent, string, StreamPosition, LogPosition> Decode(BsonDocument doc)
        {
            var logPosition = LogPosition.FromInt64(doc["_id"].AsInt64);
            var streamId = doc[EventLogEntry.FieldNames.StreamId].AsString;
            var streamPosition = StreamPosition.FromInt64(doc[EventLogEntry.FieldNames.StreamPosition].AsInt64);
            var eventName = doc[EventLogEntry.FieldNames.EventName].AsString;
            var eventData = doc[EventLogEntry.FieldNames.EventData];
            // Reads that exclude metadata project the field out of the document.
            var rawMetadata = doc.GetValue(EventLogEntry.FieldNames.Metadata, BsonNull.Value);
            var createdAtUtc = doc[EventLogEntry.FieldNames.CreatedAtUtc].AsBsonDateTime.ToUniversalTime();

            var (payload, metadata) = decoder.Decode(eventName, eventData, rawMetadata);

            var context = ReadEventContext.Create(
                streamId,
                metadata,
                createdAtUtc,
                streamPosition,
                logPosition);

            return ReadEvent.Create(payload, context);
        }
    }
}