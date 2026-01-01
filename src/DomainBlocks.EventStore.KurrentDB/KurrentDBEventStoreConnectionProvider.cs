using DomainBlocks.EventStore.Abstractions;
using KurrentDB.Client;

namespace DomainBlocks.EventStore.KurrentDB;

public sealed class KurrentDBEventStoreConnectionProvider(KurrentDBClient client) :
    IKurrentDBEventStoreConnectionProvider
{
    private readonly KurrentDBEventStoreConnection _connection = new(client);
    private int _disposed;

    public static KurrentDBEventStoreConnectionProvider FromConnectionString(string connectionString)
    {
        var settings = KurrentDBClientSettings.Create(connectionString);
        return FromSettings(settings);
    }

    public static KurrentDBEventStoreConnectionProvider FromSettings(KurrentDBClientSettings settings)
    {
        var client = new KurrentDBClient(settings);
        return new KurrentDBEventStoreConnectionProvider(client);
    }

    public ValueTask<ConnectionScope<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>> AcquireAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var scope = ConnectionScope.Create(_connection);
        return ValueTask.FromResult(scope);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await client.DisposeAsync().ConfigureAwait(false);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed != 0, this);
}