using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.Testing.Integration.MongoDB;

/// <summary>
/// The MongoDB replica set shared by every fixture in a test assembly. An assembly's <c>[SetUpFixture]</c> starts and
/// stops it, and fixtures reach it through the static members.
/// </summary>
public static class MongoTestEnvironment
{
    public static MongoReplicaSet ReplicaSet { get; private set; } = null!;

    public static IMongoClient MongoClient { get; private set; } = null!;

    /// <summary>
    /// The replica set's connection string, for tests that create their own client.
    /// </summary>
    public static string ConnectionString { get; private set; } = null!;

    public static ILoggerFactory LoggerFactory { get; private set; } = null!;

    public static async Task StartAsync()
    {
        ReplicaSet = await MongoReplicaSet.CreateAsync();
        ConnectionString = ReplicaSet.ConnectionString;
        MongoClient = new MongoClient(ConnectionString);
        LoggerFactory = TestLoggerFactory.Create();
        TestMongoSerialization.Configure();
    }

    /// <summary>
    /// Returns the replica set's connection string with its server replaced by a proxy's address.
    /// </summary>
    public static string GetConnectionString(string proxyHostname, int proxyPort)
    {
        var builder = new MongoUrlBuilder(ReplicaSet.ConnectionString)
        {
            Server = new MongoServerAddress(proxyHostname, proxyPort)
        };

        return builder.ToMongoUrl().ToString();
    }

    /// <summary>
    /// Routes <see cref="ConnectionString"/> and <see cref="MongoClient"/> through a proxy in front of the replica set,
    /// such as one that adds network latency. Call it before any fixture connects.
    /// </summary>
    public static void UseProxy(string proxyHostname, int proxyPort)
    {
        ConnectionString = GetConnectionString(proxyHostname, proxyPort);
        MongoClient.Dispose();
        MongoClient = new MongoClient(ConnectionString);
    }

    public static async Task StopAsync()
    {
        LoggerFactory.Dispose();
        MongoClient.Dispose();

        if (ReplicaSet is not null)
            await ReplicaSet.DisposeAsync();
    }

    public static async Task<string> GetServerVersionAsync()
    {
        var buildInfo = await MongoClient
            .GetDatabase("admin")
            .RunCommandAsync<BsonDocument>(new BsonDocument("buildInfo", 1));

        return buildInfo["version"].AsString;
    }
}