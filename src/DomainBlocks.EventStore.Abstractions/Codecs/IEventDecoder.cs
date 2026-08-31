namespace DomainBlocks.EventStore.Abstractions.Codecs;

public interface IEventDecoder<TEvent, in TEventData, in TMetadata> where TEvent : notnull where TEventData : notnull
{
    DecodedEvent<TEvent> Decode(string eventName, TEventData eventData, TMetadata? metadata);
}