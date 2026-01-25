namespace DomainBlocks.EventStore.Abstractions;

public sealed class EventCodec<TEvent, TEventData, TMetadata> where TEvent : notnull where TEventData : notnull
{
    public required IEventEncoder<TEvent, TEventData, TMetadata> Encoder { get; init; }
    public required IEventDecoder<TEvent, TEventData, TMetadata> Decoder { get; init; }
}