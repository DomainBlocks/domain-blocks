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
    Func<ValueTask>? asyncDispose = null) :
    IAsyncDisposable
    where TEventData : notnull
    where TMetadata : notnull
{
    private int _disposed;

    public IEventStoreConnection<TEventData, TMetadata> Connection
    {
        get
        {
            ThrowIfDisposed();
            return field;
        }
    } = connection;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        if (asyncDispose != null)
            await asyncDispose().ConfigureAwait(false);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed != 0, this);
}