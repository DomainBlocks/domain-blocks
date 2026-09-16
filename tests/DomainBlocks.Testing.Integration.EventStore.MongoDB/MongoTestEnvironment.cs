using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace DomainBlocks.Testing.Integration.EventStore.MongoDB;

/// <summary>
/// The MongoDB replica set shared by every fixture in a test assembly. An assembly's <c>[SetUpFixture]</c> starts and
/// stops it; fixtures reach it through the static members.
/// </summary>
public static class MongoTestEnvironment
{
    private static MongoReplicaSet? _replicaSet;

    public static IMongoClient MongoClient { get; private set; } = null!;

    /// <summary>
    /// The replica set's connection string, for tests that create their own client.
    /// </summary>
    public static string ConnectionString { get; private set; } = null!;

    public static ILoggerFactory LoggerFactory { get; private set; } = null!;

    public static async Task StartAsync()
    {
        _replicaSet = await MongoReplicaSet.CreateAsync();
        ConnectionString = _replicaSet.ConnectionString;
        MongoClient = new MongoClient(ConnectionString);
        LoggerFactory = TestLoggerFactory.Create();
        TestMongoSerialization.Configure();
    }

    public static async Task StopAsync()
    {
        LoggerFactory.Dispose();
        MongoClient.Dispose();

        if (_replicaSet is not null)
            await _replicaSet.DisposeAsync();
    }
}