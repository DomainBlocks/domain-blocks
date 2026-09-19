using DomainBlocks.Serialization.SystemTextJson;
using KurrentDB.Client;

namespace DomainBlocks.EventStore.KurrentDB;

public sealed class KurrentDBEventStoreBuilder<TEvent> :
    EventStoreBuilder<TEvent, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>, KurrentDBEventStoreBuilder<TEvent>>
    where TEvent : notnull
{
    private KurrentDBClient? _client;
    private KurrentDBClientSettings? _clientSettings;

    public KurrentDBEventStoreBuilder<TEvent> UseClient(KurrentDBClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        _clientSettings = null;
        _client = client;
        return this;
    }

    public KurrentDBEventStoreBuilder<TEvent> UseClientSettings(KurrentDBClientSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _clientSettings = settings;
        _client = null;
        return this;
    }

    public KurrentDBEventStoreBuilder<TEvent> UseConnectionString(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return UseClientSettings(KurrentDBClientSettings.Create(connectionString));
    }

    public IEventStore<TEvent, string, global::KurrentDB.Client.StreamPosition, Position> Build()
    {
        if (_client is null && _clientSettings is null)
        {
            throw new InvalidOperationException(
                "No connection is configured. Call UseClient, UseClientSettings, or UseConnectionString.");
        }

        var codec = BuildCodec(
            static () => new JsonUtf8BytesObjectSerializer(),
            static () => new JsonUtf8BytesMetadataSerializer());

        KurrentDBClient? ownedClient = null;
        var client = _client ?? (ownedClient = new KurrentDBClient(_clientSettings));

        try
        {
            return Decorate(new KurrentDBEventStore<TEvent>(client, codec, ownedClient));
        }
        catch
        {
            ownedClient?.Dispose();
            throw;
        }
    }
}