namespace DomainBlocks.EventStore.Abstractions;

public interface IEventEncoder<TEvent, TEventData, TMetadata> where TEvent : notnull where TEventData : notnull
{
    IEventEncodingSession<TEvent, TEventData, TMetadata> CreateSession();
}