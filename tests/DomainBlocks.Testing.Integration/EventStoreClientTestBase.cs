namespace DomainBlocks.Testing.Integration;

public abstract class EventStoreClientTestBase<TEvent> where TEvent : notnull
{
    protected abstract Task<ITestEventStoreClientFactory<TEvent>> GetClientFactoryAsync(
        CancellationToken cancellationToken = default);
}