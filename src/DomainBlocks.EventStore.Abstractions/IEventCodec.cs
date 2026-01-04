namespace DomainBlocks.EventStore.Abstractions;

public interface IEventCodec<TEventBase, TEventData, TMetadata>
    where TEventBase : class
    where TEventData : notnull
    where TMetadata : notnull
{
    IEventEncoder<TEventBase, TEventData, TMetadata> CreateEncoder();

    ReadEvent<TEventBase> Decode(
        string eventName,
        TEventData eventData,
        TMetadata? metadata,
        ReadEventContext context);
}