namespace DomainBlocks.Testing.Integration;

public abstract class EventStoreClientTestBase<TEvent> where TEvent : notnull
{
    // Set a longer timeout when debugging.
#if DEBUG
    protected const int TestTimeoutMillis = 10 * 60 * 1_000;
#else
    protected const int TestTimeoutMillis = 120 * 1_000;
#endif

    protected abstract Task<ITestEventStoreClientFactory<TEvent>> GetClientFactoryAsync(
        CancellationToken cancellationToken = default);
}