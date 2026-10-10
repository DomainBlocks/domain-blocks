namespace DomainBlocks.EventStore.Codecs;

/// <summary>
/// Defines how a store decodes a stored record into an event and its metadata.
/// </summary>
public interface IEventDecoder<TEvent, in TEventData, in TMetadata> where TEvent : notnull where TEventData : notnull
{
    /// <param name="eventName">The name the event is stored under.</param>
    /// <param name="eventData">The stored event data.</param>
    /// <param name="metadata">
    /// The stored metadata. If there is none, or the read excluded it, this is <see langword="null"/> or the store's
    /// own null value, such as <c>BsonNull</c>.
    /// </param>
    DecodedEvent<TEvent> Decode(string eventName, TEventData eventData, TMetadata? metadata);
}