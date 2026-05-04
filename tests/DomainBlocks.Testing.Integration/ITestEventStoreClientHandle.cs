using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.Testing.Integration;

public interface ITestEventStoreClientHandle<TEvent> : IAsyncDisposable where TEvent : notnull
{
    IEventStoreClient<TEvent> Client { get; }
}