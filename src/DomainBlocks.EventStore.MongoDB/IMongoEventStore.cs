using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.MongoDB;

public interface IMongoEventStore<TEvent> :
    IEventStore<TEvent, string, StreamPosition, LogPosition>,
    IAsyncDisposable
    where TEvent : notnull;