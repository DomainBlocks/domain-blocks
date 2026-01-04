namespace DomainBlocks.EventStore.Abstractions;

public interface IEventCodec<TEvent, TEventData, TMetadata> where TEvent : notnull where TEventData : notnull
{
    IEventEncoder<TEvent, TEventData, TMetadata> CreateEncoder();

    DecodedEvent<TEvent> Decode(string eventName, TEventData eventData, TMetadata? metadata);
}