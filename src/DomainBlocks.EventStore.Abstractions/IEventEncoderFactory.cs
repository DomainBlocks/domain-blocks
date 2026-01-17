namespace DomainBlocks.EventStore.Abstractions;

public interface IEventEncoderFactory<TEvent, TEventData, TMetadata> where TEvent : notnull where TEventData : notnull
{
    IEventEncoder<TEvent, TEventData, TMetadata> CreateEncoder();
}