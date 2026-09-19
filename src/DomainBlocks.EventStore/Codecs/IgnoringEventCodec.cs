using System.Collections.Frozen;

namespace DomainBlocks.EventStore.Codecs;

internal sealed class IgnoringEventCodec<TEvent, TEventData, TMetadata>(
    IEventCodec<TEvent, TEventData, TMetadata> inner,
    FrozenSet<string> ignoredEventNames,
    TEvent sentinel) : IEventCodec<TEvent, TEventData, TMetadata>
    where TEvent : notnull
    where TEventData : notnull
{
    public EncodedEvent<TEventData, TMetadata> Encode(
        TEvent payload,
        ReadOnlySpan<KeyValuePair<string, string>> metadata) => inner.Encode(payload, metadata);

    public DecodedEvent<TEvent> Decode(string eventName, TEventData eventData, TMetadata? metadata) =>
        ignoredEventNames.Contains(eventName)
            ? DecodedEvent.Create(sentinel, FrozenDictionary<string, string>.Empty)
            : inner.Decode(eventName, eventData, metadata);
}