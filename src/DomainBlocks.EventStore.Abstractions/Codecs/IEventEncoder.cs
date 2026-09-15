namespace DomainBlocks.EventStore.Abstractions.Codecs;

/// <summary>
/// Encodes one event and its already-merged metadata into the record a store persists.
/// </summary>
public interface IEventEncoder<in TEvent, TEventData, TMetadata> where TEvent : notnull where TEventData : notnull
{
    EncodedEvent<TEventData, TMetadata> Encode(TEvent payload, ReadOnlySpan<KeyValuePair<string, string>> metadata);
}