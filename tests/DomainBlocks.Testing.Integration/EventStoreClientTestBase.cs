using DomainBlocks.EventStore.Abstractions;
using NUnit.Framework;

namespace DomainBlocks.Testing.Integration;

public abstract class EventStoreClientTestBase<TEvent> where TEvent : notnull
{
    private ITestEventStoreClientFactory<TEvent>? _clientFactory;
    private ITestEventStoreClientHandle<TEvent>? _clientHandle;

    protected ITestEventStoreClientFactory<TEvent> ClientFactory { get; private set; } = null!;
    protected IEventStoreClient<TEvent> Client { get; private set; } = null!;

    [SetUp]
    public async Task SetUp()
    {
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        _clientFactory = await GetClientFactoryAsync(ct.Token);
        _clientHandle = await _clientFactory.CreateAsync("default", ct.Token);

        ClientFactory = _clientFactory;
        Client = _clientHandle.Client;
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_clientHandle is not null)
            await _clientHandle.DisposeAsync();

        if (_clientFactory is not null)
            await _clientFactory.DisposeAsync();
    }

    protected abstract Task<ITestEventStoreClientFactory<TEvent>> GetClientFactoryAsync(
        CancellationToken cancellationToken = default);
}