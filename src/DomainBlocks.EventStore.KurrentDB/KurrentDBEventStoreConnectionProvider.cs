using DomainBlocks.EventStore.Abstractions;
using KurrentDB.Client;

namespace DomainBlocks.EventStore.KurrentDB;

public sealed class KurrentDBEventStoreConnectionProvider(KurrentDBClient client) :
    IKurrentDBEventStoreConnectionProvider
{
    private readonly KurrentDBEventStoreConnection _connection = new(client);

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
        var scope = ConnectionScope.Create(_connection);
        return ValueTask.FromResult(scope);
    }

    public ValueTask DisposeAsync() => client.DisposeAsync();
}