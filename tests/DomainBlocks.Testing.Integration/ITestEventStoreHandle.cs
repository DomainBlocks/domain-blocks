using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.Testing.Integration;

public interface ITestEventStoreHandle<TEvent, TStreamId, TStreamPos, TLogPos> :
    IAsyncDisposable
    where TEvent : notnull
    where TStreamId : notnull
    where TStreamPos : notnull
    where TLogPos : notnull
{
    IEventStore<TEvent, TStreamId, TStreamPos, TLogPos> Instance { get; }
}