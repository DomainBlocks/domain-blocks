namespace DomainBlocks.EventStore.Codecs;

/// <summary>
/// Decodes one persisted record into an event and its metadata.
/// </summary>
public interface IEventDecoder<TEvent, in TEventData, in TMetadata> where TEvent : notnull where TEventData : notnull
{
    /// <param name="eventName">The stored name of the event.</param>
    /// <param name="eventData">The stored event data.</param>
    /// <param name="metadata">
    /// The stored metadata, or <see langword="default"/> when the record has none or the read excluded it.
    /// </param>
    DecodedEvent<TEvent> Decode(string eventName, TEventData eventData, TMetadata? metadata);
}