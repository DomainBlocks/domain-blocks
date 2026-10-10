namespace DomainBlocks.EventStore.Codecs;

/// <summary>
/// Defines how a store encodes events into stored records and decodes them back.
/// </summary>
/// <typeparam name="TEvent">The base type of the events.</typeparam>
/// <typeparam name="TEventData">The store's representation of event data.</typeparam>
/// <typeparam name="TMetadata">The store's representation of metadata.</typeparam>
public interface IEventCodec<TEvent, TEventData, TMetadata> :
    IEventEncoder<TEvent, TEventData, TMetadata>,
    IEventDecoder<TEvent, TEventData, TMetadata>
    where TEvent : notnull
    where TEventData : notnull;