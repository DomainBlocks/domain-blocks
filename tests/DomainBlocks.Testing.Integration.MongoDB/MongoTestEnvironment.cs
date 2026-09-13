using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace DomainBlocks.Testing.Integration.MongoDB;

/// <summary>
/// The MongoDB replica set shared by every fixture in a test assembly. An assembly's <c>[SetUpFixture]</c> starts and
/// stops it; fixtures reach it through the static members.
/// </summary>
public static class MongoTestEnvironment
{
    private static MongoReplicaSet? _replicaSet;

    public static IMongoClient MongoClient { get; private set; } = null!;

    public static ILoggerFactory LoggerFactory { get; private set; } = null!;

    public static async Task StartAsync()
    {
        _replicaSet = await MongoReplicaSet.CreateAsync();
        MongoClient = new MongoClient(_replicaSet.ConnectionString);
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