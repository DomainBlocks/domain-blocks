using DomainBlocks.EventStore.Codecs;
using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB;

internal static class ReadEventExtensions
{
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