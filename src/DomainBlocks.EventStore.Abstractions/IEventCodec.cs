namespace DomainBlocks.EventStore.Abstractions;

public interface IEventCodec<TEvent, TEventData, TMetadata> :
    IEventEncoderFactory<TEvent, TEventData, TMetadata>,
    IEventDecoder<TEvent, TEventData, TMetadata>
    where TEvent : notnull where TEventData : notnull;