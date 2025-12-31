using System.Collections.Frozen;
using DomainBlocks.EventStore.Abstractions.Events;

namespace DomainBlocks.EventStore.MongoDB;

/// <summary>
/// Provides conversion between event wrappers and the default event document for Mongo persistence.
/// </summary>
public sealed class DefaultEventDocumentConverter<TSerialized> :
    IEventDocumentConverter<DefaultEventDocument<TSerialized>, TSerialized>
    where TSerialized : notnull
{
    /// <inheritdoc/>
    public DefaultEventDocument<TSerialized> ToEventDocument(
        SerializedAppendEvent<TSerialized> @event,
        string streamId,
        StreamVersion streamVersion,
        DateTime createdAt)
    {
        return new DefaultEventDocument<TSerialized>
        {
            StreamId = streamId,
            StreamVersion = streamVersion.Value,
            EventName = @event.EventName,
            CreatedAt = createdAt,
            EventData = @event.EventData,
            Metadata = @event.Metadata
        };
    }

    /// <inheritdoc/>
    public ReadEvent<TSerialized> FromEventDocument(DefaultEventDocument<TSerialized> document)
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