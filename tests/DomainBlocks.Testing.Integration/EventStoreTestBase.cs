using DomainBlocks.EventStore.Abstractions;
using NUnit.Framework;

namespace DomainBlocks.Testing.Integration;

public abstract class EventStoreTestBase<TEvent, TStreamId, TStreamPos, TLogPos>
    where TEvent : notnull
    where TStreamId : notnull
    where TStreamPos : notnull
    where TLogPos : notnull
{
    private const long SetUpTimeoutSeconds = 5;

    private ITestEventStoreFactory<TEvent, TStreamId, TStreamPos, TLogPos>? _eventStoreFactory;
    private ITestEventStoreHandle<TEvent, TStreamId, TStreamPos, TLogPos>? _eventStoreHandle;

    protected ITestEventStoreFactory<TEvent, TStreamId, TStreamPos, TLogPos> EventStoreFactory { get; private set; } =
        null!;

    protected IEventStore<TEvent, TStreamId, TStreamPos, TLogPos> EventStore { get; private set; } = null!;

    [SetUp]
    public async Task SetUp()
    {
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(SetUpTimeoutSeconds));

        _eventStoreFactory = await GetEventStoreFactoryAsync(ct.Token);
        _eventStoreHandle = await _eventStoreFactory.CreateAsync("default", ct.Token);

        EventStoreFactory = _eventStoreFactory;
        EventStore = _eventStoreHandle.Instance;
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_eventStoreHandle is not null)
            await _eventStoreHandle.DisposeAsync();

        if (_eventStoreFactory is not null)
            await _eventStoreFactory.DisposeAsync();
    }

    protected abstract Task<ITestEventStoreFactory<TEvent, TStreamId, TStreamPos, TLogPos>> GetEventStoreFactoryAsync(
        CancellationToken cancellationToken = default);

    protected virtual TStreamPos CreateStreamPosition(ulong value) => throw new NotImplementedException();
}