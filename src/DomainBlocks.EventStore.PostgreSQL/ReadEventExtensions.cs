using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Abstractions.Codecs;

namespace DomainBlocks.EventStore.PostgreSQL;

internal static class ReadEventExtensions
{
    extension<TEvent>(IEventDecoder<TEvent, PostgresEventData, string> decoder) where TEvent : notnull
    {
        public ReadEvent<TEvent, string, StreamPosition, LogPosition> Decode(EventLogRow row)
        {
            var (payload, metadata) = decoder.Decode(row.EventName, row.EventData, row.Metadata);

            var context = ReadEventContext.Create(
                row.StreamId,
                metadata,
                row.CreatedAt,
                StreamPosition.FromInt64(row.StreamPosition),
                LogPosition.FromInt64(row.Position));

            return ReadEvent.Create(payload, context);
        }
    }
}
