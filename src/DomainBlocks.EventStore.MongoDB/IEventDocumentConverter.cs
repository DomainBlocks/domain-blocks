using DomainBlocks.EventStore.Abstractions.Events;

namespace DomainBlocks.EventStore.MongoDB;

/// <summary>
/// Provides conversion between event wrappers and event documents for Mongo persistence.
/// </summary>
/// <typeparam name="TEventDocument">The document type used to store events.</typeparam>
/// <typeparam name="TEventData">The event data type to store.</typeparam>
/// <typeparam name="TMetadata">The metadata type to store.</typeparam>
public interface IEventDocumentConverter<TEventDocument, TEventData, TMetadata>
    where TEventData : notnull
    where TMetadata : notnull
{
    /// <summary>
    /// Converts an append event into an event document.
    /// </summary>
    /// <param name="event">The event to append.</param>
    /// <param name="streamId">The identifier of the event stream.</param>
    /// <param name="streamVersion">The version of the stream for this event.</param>
    /// <param name="createdAt">The UTC timestamp at which this event was created.</param>
    /// <returns>An event document representing the specified event.</returns>
    TEventDocument ToEventDocument(
        AppendEvent<TEventData, TMetadata> @event,
        string streamId,
        StreamVersion streamVersion,
        DateTime createdAt);

    /// <summary>
    /// Converts an event document into a read event.
    /// </summary>
    /// <param name="document">The event document to convert.</param>
    /// <returns>A read event reconstructed from the specified event document.</returns>
    ReadEvent<TEventData, TMetadata> FromEventDocument(TEventDocument document);
}