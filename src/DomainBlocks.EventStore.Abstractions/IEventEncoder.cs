namespace DomainBlocks.EventStore.Abstractions;

public interface IEventEncoder<TEvent, TEventData, TMetadata> where TEvent : notnull where TEventData : notnull
{
    EncodedEvent<TEventData, TMetadata> Encode(AppendEvent<TEvent> appendEvent);
}