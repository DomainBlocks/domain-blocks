using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Abstractions.Events;

namespace DomainBlocks.EventStore.MongoDB;

/// <summary>
/// Provides conversion between uncommitted/committed event wrappers and the default event document for Mongo
/// persistence.
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
        DateTime committedAt)
    {
        return new DefaultEventDocument<TSerialized>
        {
            StreamId = streamId,
            StreamVersion = streamVersion.ToInt64(),
            EventName = @event.Header.EventName,
            Metadata = @event.Header.Metadata,
            CommittedAt = committedAt,
            Value = @event.Value
        };
    }

    /// <inheritdoc/>
    public CommittedEvent<TSerialized> FromEventDocument(DefaultEventDocument<TSerialized> document)
    {
        return CommittedEvent.Create(
            new CommittedEventHeader(
                document.StreamId,
                StreamVersion.FromInt64(document.StreamVersion),
                document.EventName,
                document.Metadata,
                document.CommittedAt),
            document.Value);
    }
}