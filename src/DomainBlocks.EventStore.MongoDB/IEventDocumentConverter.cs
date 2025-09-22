using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.MongoDB;

/// <summary>
/// Provides conversion between uncommitted/committed event wrappers and event documents for Mongo persistence.
/// </summary>
/// <typeparam name="TEventDocument">The document type used to store events.</typeparam>
public interface IEventDocumentConverter<TEventDocument>
{
    /// <summary>
    /// Converts an uncommitted event into an event document.
    /// </summary>
    /// <param name="event">The uncommitted event.</param>
    /// <param name="streamId">The identifier of the event stream.</param>
    /// <param name="streamVersion">The version of the stream for this event.</param>
    /// <param name="committedAt">The UTC timestamp at which this event is committed.</param>
    /// <typeparam name="TPayload">The payload type of the event.</typeparam>
    /// <returns>An event document representing the specified event.</returns>
    TEventDocument ToEventDocument<TPayload>(
        UncommittedEvent<TPayload> @event,
        string streamId,
        StreamVersion streamVersion,
        DateTime committedAt) where TPayload : notnull;

    /// <summary>
    /// Converts an event document into a committed event.
    /// </summary>
    /// <param name="document">The event document to convert.</param>
    /// <typeparam name="TPayload">The expected payload type of the event.</typeparam>
    /// <returns>A committed event reconstructed from the specified event document.</returns>
    CommittedEvent<TPayload> FromEventDocument<TPayload>(TEventDocument document) where TPayload : notnull;
}