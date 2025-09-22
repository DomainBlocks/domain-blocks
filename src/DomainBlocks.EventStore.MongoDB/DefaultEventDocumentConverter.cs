using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.MongoDB;

/// <summary>
/// Provides conversion between uncommitted/committed event wrappers and the default event document for Mongo
/// persistence.
/// </summary>
public sealed class DefaultEventDocumentConverter : IEventDocumentConverter<DefaultEventDocument>
{
    /// <inheritdoc/>
    public DefaultEventDocument ToEventDocument<TPayload>(
        UncommittedEvent<TPayload> @event,
        string streamId,
        StreamVersion streamVersion,
        DateTime committedAt) where TPayload : notnull
    {
        return new DefaultEventDocument
        {
            StreamId = streamId,
            StreamVersion = streamVersion.ToInt64(),
            EventName = @event.Header.EventName,
            Metadata = @event.Header.Metadata,
            CommittedAt = committedAt,
            Payload = BsonPayloadConverter.ToBsonValue(@event.Payload)
        };
    }

    /// <inheritdoc/>
    public CommittedEvent<TPayload> FromEventDocument<TPayload>(DefaultEventDocument document) where TPayload : notnull
    {
        return CommittedEvent.Create(
            new CommittedEventHeader(
                document.StreamId,
                StreamVersion.FromInt64(document.StreamVersion),
                document.EventName,
                document.Metadata,
                document.CommittedAt),
            BsonPayloadConverter.FromBsonValue<TPayload>(document.Payload));
    }
}