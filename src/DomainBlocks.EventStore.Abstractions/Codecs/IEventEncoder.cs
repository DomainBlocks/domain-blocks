namespace DomainBlocks.EventStore.Abstractions.Codecs;

public interface IEventEncoder<TEvent, TEventData, TMetadata> where TEvent : notnull where TEventData : notnull
{
    IEnumerable<EncodedEvent<TEventData, TMetadata>> Encode(IEnumerable<AppendEvent<TEvent>> events);
}