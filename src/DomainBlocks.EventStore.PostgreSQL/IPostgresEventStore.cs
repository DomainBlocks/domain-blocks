using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.PostgreSQL;

public interface IPostgresEventStore<TEvent> :
    IEventStore<TEvent, string, StreamPosition, LogPosition>,
    IAsyncDisposable
    where TEvent : notnull;
