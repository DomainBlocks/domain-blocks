namespace DomainBlocks.EventStore.Codecs;

/// <summary>
/// Translates between events and the records a store persists, in both directions. This is the only thing a store
/// needs to know about events: how to name and serialize them on the way in, and how to resolve and deserialize them
/// on the way out.
/// </summary>
/// <typeparam name="TEvent">The event base type.</typeparam>
/// <typeparam name="TEventData">The store's representation of event data.</typeparam>
/// <typeparam name="TMetadata">The store's representation of metadata.</typeparam>
public interface IEventCodec<TEvent, TEventData, TMetadata> :
    IEventEncoder<TEvent, TEventData, TMetadata>,
    IEventDecoder<TEvent, TEventData, TMetadata>
    where TEvent : notnull
    where TEventData : notnull;