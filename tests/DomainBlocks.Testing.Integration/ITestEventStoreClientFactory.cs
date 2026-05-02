namespace DomainBlocks.Testing.Integration;

public interface ITestEventStoreClientFactory<TEvent> : IAsyncDisposable where TEvent : notnull
{
    Task<ITestEventStoreClientHandle<TEvent>> CreateAsync(string name, CancellationToken cancellationToken = default);
}