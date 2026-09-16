namespace DomainBlocks.EventStore.Codecs;

public static class EventEncoderExtensions
{
    extension<TEvent, TEventData, TMetadata>(IEventEncoder<TEvent, TEventData, TMetadata> encoder)
        where TEvent : notnull
        where TEventData : notnull
    {
        /// <summary>
        /// Encodes a sequence of events lazily, in order.
        /// </summary>
        public IEnumerable<EncodedEvent<TEventData, TMetadata>> Encode(IEnumerable<AppendableEvent<TEvent>> events)
        {
            foreach (var @event in events)
                yield return encoder.Encode(@event.Payload, @event.Metadata);
        }
    }
}