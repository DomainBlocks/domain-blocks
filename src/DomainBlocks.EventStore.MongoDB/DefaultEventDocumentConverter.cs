using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Abstractions.Events;

namespace DomainBlocks.EventStore.MongoDB;

/// <summary>
/// Provides conversion between uncommitted/committed event wrappers and the default event document for Mongo
/// persistence.
/// </summary>
public sealed class DefaultEventDocumentConverter<TPayload> :
    IEventDocumentConverter<DefaultEventDocument<TPayload>, TPayload>
    where TPayload : notnull
{
    /// <inheritdoc/>
    public DefaultEventDocument<TPayload> ToEventDocument(
        UncommittedEvent<TPayload> @event,
        string streamId,
        StreamVersion streamVersion,
        DateTime committedAt)
    {
        return new DefaultEventDocument<TPayload>
        {
            StreamId = streamId,
            StreamVersion = streamVersion.ToInt64(),
            EventName = @event.Header.EventName,
            Metadata = @event.Header.Metadata,
            CommittedAt = committedAt,
            Payload = @event.Payload
        };
    }

    /// <inheritdoc/>
    public CommittedEvent<TPayload> FromEventDocument(DefaultEventDocument<TPayload> document)
    {
        return CommittedEvent.Create(
            new CommittedEventHeader(
                document.StreamId,
                StreamVersion.FromInt64(document.StreamVersion),
                document.EventName,
                document.Metadata,
                document.CommittedAt),
            document.Payload);
    }
}