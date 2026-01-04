namespace DomainBlocks.EventStore.Abstractions;

public interface IEventEncoder<TEventBase, TEventData, TMetadata>
    where TEventBase : class
    where TEventData : notnull
    where TMetadata : notnull
{
    EncodedEvent<TEventData, TMetadata> Encode(AppendEvent<TEventBase> appendEvent);
}