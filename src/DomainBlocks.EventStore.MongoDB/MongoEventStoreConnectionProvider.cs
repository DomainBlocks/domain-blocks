using DomainBlocks.EventStore.Abstractions;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

public static class MongoEventStoreConnectionProvider
{
    public static MongoEventStoreConnectionProvider<TEventDocument, TEventData, TMetadata>
        FromConnectionString<TEventDocument, TEventData, TMetadata>(
            string connectionString,
            MongoEventStoreConnectionOptions<TEventDocument, TEventData, TMetadata> options)
        where TEventData : notnull
        where TMetadata : notnull
    {
        var settings = MongoClientSettings.FromConnectionString(connectionString);
        return FromSettings(settings, options);
    }

    public static MongoEventStoreConnectionProvider<TEventDocument, TEventData, TMetadata>
        FromSettings<TEventDocument, TEventData, TMetadata>(
            MongoClientSettings settings,
            MongoEventStoreConnectionOptions<TEventDocument, TEventData, TMetadata> options)
        where TEventData : notnull
        where TMetadata : notnull
    {
        var client = new MongoClient(settings);
        return new MongoEventStoreConnectionProvider<TEventDocument, TEventData, TMetadata>(client, options);
    }
}

public sealed class MongoEventStoreConnectionProvider<TEventDocument, TEventData, TMetadata> :
    IMongoEventStoreConnectionProvider<TEventData, TMetadata>
    where TEventData : notnull
    where TMetadata : notnull
{
    private readonly IMongoClient _client;
    private readonly MongoEventStoreConnection<TEventDocument, TEventData, TMetadata> _connection;

    public MongoEventStoreConnectionProvider(
        IMongoClient client,
        MongoEventStoreConnectionOptions<TEventDocument, TEventData, TMetadata> options)
    {
        var ns = options.CollectionNamespace;
        var database = client.GetDatabase(ns.DatabaseNamespace.DatabaseName);
        var collection = database.GetCollection<TEventDocument>(ns.CollectionName);

        _client = client;
        _connection = new MongoEventStoreConnection<TEventDocument, TEventData, TMetadata>(collection, options);
    }

    public ValueTask<ConnectionScope<TEventData, TMetadata>> AcquireAsync(
        CancellationToken cancellationToken = default)
    {
        var scope = ConnectionScope.Create(_connection);
        return ValueTask.FromResult(scope);
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }
}