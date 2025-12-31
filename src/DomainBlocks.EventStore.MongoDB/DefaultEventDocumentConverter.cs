using System.Collections.Frozen;
using DomainBlocks.EventStore.Abstractions.Events;
using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB;

/// <summary>
/// Provides conversion between event wrappers and the default event document for Mongo persistence.
/// </summary>
public sealed class DefaultEventDocumentConverter : IEventDocumentConverter<DefaultEventDocument, BsonValue, BsonValue>
{
    /// <inheritdoc/>
    public DefaultEventDocument ToEventDocument(
        AppendEvent<BsonValue, BsonValue> @event,
        string streamId,
        StreamVersion streamVersion,
        DateTime createdAt)
    {
        return new DefaultEventDocument
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
    public ReadEvent<BsonValue> FromEventDocument(DefaultEventDocument document)
    {
        return ReadEvent.Create(
            new ReadEventHeader(
                document.StreamId,
                new StreamVersion(document.StreamVersion),
                document.EventName,
                // TODO: Deal with metadata for reads (will address in a future PR),
                FrozenDictionary<string, string>.Empty,
                document.CreatedAt),
            document.EventData);
    }
}