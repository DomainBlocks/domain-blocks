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
        UncommittedEvent<TSerialized> @event,
        string streamId,
        StreamVersion streamVersion,
        DateTime createdAt)
    {
        return new DefaultEventDocument<TSerialized>
        {
            StreamId = streamId,
            StreamVersion = streamVersion.Value,
            EventName = @event.Header.EventName,
            Metadata = @event.Header.Metadata,
            CreatedAt = createdAt,
            Value = @event.Value
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
                document.Metadata,
                document.CreatedAt),
            document.Value);
    }
}