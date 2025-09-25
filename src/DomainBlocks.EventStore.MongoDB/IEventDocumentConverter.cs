using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.MongoDB;

/// <summary>
/// Provides conversion between uncommitted/committed event wrappers and event documents for Mongo persistence.
/// </summary>
/// <typeparam name="TEventDocument">The document type used to store events.</typeparam>
/// <typeparam name="TPayload">The event payload type.</typeparam>
public interface IEventDocumentConverter<TEventDocument, TPayload> where TPayload : notnull
{
    /// <summary>
    /// Converts an uncommitted event into an event document.
    /// </summary>
    /// <param name="event">The uncommitted event.</param>
    /// <param name="streamId">The identifier of the event stream.</param>
    /// <param name="streamVersion">The version of the stream for this event.</param>
    /// <param name="committedAt">The UTC timestamp at which this event is committed.</param>
    /// <returns>An event document representing the specified event.</returns>
    TEventDocument ToEventDocument(
        UncommittedEvent<TPayload> @event,
        string streamId,
        StreamVersion streamVersion,
        DateTime committedAt);

    /// <summary>
    /// Converts an event document into a committed event.
    /// </summary>
    /// <param name="document">The event document to convert.</param>
    /// <returns>A committed event reconstructed from the specified event document.</returns>
    CommittedEvent<TPayload> FromEventDocument(TEventDocument document);
}