using DomainBlocks.EventStore.Abstractions;
using NUnit.Framework;

namespace DomainBlocks.Testing.Integration;

public abstract class EventStoreClientTestBase<TEvent> where TEvent : notnull
{
    private ITestEventStoreClientHandle<TEvent> _clientHandle = null!;

    protected ITestEventStoreClientFactory<TEvent> ClientFactory { get; private set; } = null!;
    protected IEventStoreClient<TEvent> Client { get; private set; } = null!;

    [SetUp]
    public async Task SetUp()
    {
        using var ct = new CancellationTokenSource(TestTimeouts.DefaultMillis);

        var clientFactory = await GetClientFactoryAsync(ct.Token);
        _clientHandle = await clientFactory.CreateAsync("default", ct.Token);

        ClientFactory = clientFactory;
        Client = _clientHandle.Client;
    }

    [TearDown]
    public async Task TearDown()
    {
        await _clientHandle.DisposeAsync();
        await ClientFactory.DisposeAsync();
    }

    protected abstract Task<ITestEventStoreClientFactory<TEvent>> GetClientFactoryAsync(
        CancellationToken cancellationToken = default);
}