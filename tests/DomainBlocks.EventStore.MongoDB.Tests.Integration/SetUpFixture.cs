using DomainBlocks.Testing;
using DomainBlocks.Testing.Integration.MongoDB;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using NUnit.Framework;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

[SetUpFixture]
public class SetUpFixture
{
    private static MongoReplicaSet _mongoReplicaSet = null!;

    public static IMongoClient MongoClient { get; private set; } = null!;

    public static ILoggerFactory LoggerFactory { get; private set; } = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _mongoReplicaSet = await MongoReplicaSet.CreateAsync();

        MongoClient = new MongoClient(_mongoReplicaSet.ConnectionString);

        LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(x => x
            .AddProvider(new NUnitLoggerProvider())
            .SetMinimumLevel(LogLevel.Debug));

        TestMongoSerialization.Configure();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        LoggerFactory.Dispose();
        MongoClient.Dispose();
        await _mongoReplicaSet.DisposeAsync();
    }
}