namespace DomainBlocks.EventStore.Abstractions;

public interface IEventEncoder<TEvent, TEventData, TMetadata> where TEvent : notnull where TEventData : notnull
{
    IEnumerable<EncodedEvent<TEventData, TMetadata>> Encode(Guid commitId, IEnumerable<AppendEvent<TEvent>> events);
}