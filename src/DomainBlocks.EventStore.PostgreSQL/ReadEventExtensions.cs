using DomainBlocks.EventStore.Codecs;

namespace DomainBlocks.EventStore.PostgreSQL;

internal static class ReadEventExtensions
{
    extension<TEvent>(IEventDecoder<TEvent, PostgresEventData, string> decoder) where TEvent : notnull
    {
        public ReadEvent<TEvent, string, StreamPosition, LogPosition> Decode(
            long position,
            string streamId,
            long streamPosition,
            string eventName,
            PostgresEventData eventData,
            string? rawMetadata,
            DateTimeOffset createdAt)
        {
            var (payload, metadata) = decoder.Decode(eventName, eventData, rawMetadata);

            var context = ReadEventContext.Create(
                streamId,
                eventName,
                metadata,
                createdAt,
                StreamPosition.FromInt64(streamPosition),
                LogPosition.FromInt64(position));

            return ReadEvent.Create(payload, context);
        }
    }
}