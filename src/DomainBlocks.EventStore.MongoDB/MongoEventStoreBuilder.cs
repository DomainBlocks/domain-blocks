using DomainBlocks.Serialization.MongoDB.Bson;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

public sealed class MongoEventStoreBuilder<TEvent> :
    EventStoreBuilder<TEvent, BsonValue, BsonValue, MongoEventStoreBuilder<TEvent>>
    where TEvent : notnull
{
    private IMongoClient? _client;
    private MongoClientSettings? _clientSettings;
    private MongoEventStoreOptions _options = new();
    private ILoggerFactory? _loggerFactory;
    private ILogger? _logger;

    public MongoEventStoreBuilder<TEvent> UseClient(IMongoClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        _client = client;
        _clientSettings = null;
        return this;
    }

    public MongoEventStoreBuilder<TEvent> UseClientSettings(MongoClientSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _client = null;
        _clientSettings = settings;
        return this;
    }

    public MongoEventStoreBuilder<TEvent> UseConnectionString(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return UseClientSettings(MongoClientSettings.FromConnectionString(connectionString));
    }

    public MongoEventStoreBuilder<TEvent> UseOptions(MongoEventStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
        return this;
    }

    public MongoEventStoreBuilder<TEvent> ConfigureOptions(Action<MongoEventStoreOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        configure(_options);
        return this;
    }

    public MongoEventStoreBuilder<TEvent> UseLoggerFactory(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _loggerFactory = loggerFactory;
        _logger = null;
        return this;
    }

    public MongoEventStoreBuilder<TEvent> UseLogger(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _loggerFactory = null;
        _logger = logger;
        return this;
    }

    public IEventStore<TEvent, string, StreamPosition, LogPosition> Build()
    {
        if (_client is null && _clientSettings is null)
        {
            throw new InvalidOperationException(
                "No connection is configured. Call UseClient, UseClientSettings, or UseConnectionString.");
        }

        var codec = BuildCodec(
            static () => new BsonDocumentObjectSerializer(),
            static () => new BsonDocumentMetadataSerializer());

        var logger = _logger ?? _loggerFactory?.CreateLogger(typeof(MongoEventStore).FullName!);

        MongoClient? ownedClient = null;
        var client = _client ?? (ownedClient = new MongoClient(_clientSettings));

        try
        {
            return Decorate(MongoEventStore.Create(client, codec, _options, logger, ownedClient));
        }
        catch
        {
            ownedClient?.Dispose();
            throw;
        }
    }
}