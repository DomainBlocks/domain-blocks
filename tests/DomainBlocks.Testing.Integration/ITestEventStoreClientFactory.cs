namespace DomainBlocks.Testing.Integration;

public interface ITestEventStoreClientFactory<TEvent> : IAsyncDisposable where TEvent : notnull
{
    Task<ITestEventStoreClientHandle<TEvent>> CreateAsync(CancellationToken cancellationToken = default);
}