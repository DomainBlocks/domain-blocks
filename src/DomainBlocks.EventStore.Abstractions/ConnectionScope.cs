namespace DomainBlocks.EventStore.Abstractions;

public static class ConnectionScope
{
    public static ConnectionScope<TEventData, TMetadata> Create<TEventData, TMetadata>(
        IEventStoreConnection<TEventData, TMetadata> connection,
        Func<ValueTask>? dispose = null)
        where TEventData : notnull
        where TMetadata : notnull
    {
        return new ConnectionScope<TEventData, TMetadata>(connection, dispose);
    }
}

public sealed class ConnectionScope<TEventData, TMetadata>(
    IEventStoreConnection<TEventData, TMetadata> connection,
    Func<ValueTask>? dispose = null) :
    IAsyncDisposable
    where TEventData : notnull
    where TMetadata : notnull
{
    private Func<ValueTask>? _dispose = dispose;

    public IEventStoreConnection<TEventData, TMetadata> Connection { get; } = connection;

    public async ValueTask DisposeAsync()
    {
        var d = Interlocked.Exchange(ref _dispose, null);
        if (d is not null)
            await d().ConfigureAwait(false);
    }
}