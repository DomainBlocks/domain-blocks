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