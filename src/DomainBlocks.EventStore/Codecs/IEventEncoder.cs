namespace DomainBlocks.EventStore.Codecs;

/// <summary>
/// Defines how a store encodes an event and its metadata into a stored record.
/// </summary>
public interface IEventEncoder<in TEvent, TEventData, TMetadata> where TEvent : notnull where TEventData : notnull
{
    EncodedEvent<TEventData, TMetadata> Encode(TEvent payload, ReadOnlySpan<KeyValuePair<string, string>> metadata);
}