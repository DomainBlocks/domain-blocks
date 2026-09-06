using DomainBlocks.Testing.Integration.MongoDB;
using NUnit.Framework;

namespace DomainBlocks.EventSourcing.Tests.Integration;

[SetUpFixture]
public class SetUpFixture
{
    private static MongoReplicaSet _mongoReplicaSet = null!;

    public static string MongoConnectionString { get; private set; } = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _mongoReplicaSet = await MongoReplicaSet.CreateAsync();
        MongoConnectionString = _mongoReplicaSet.ConnectionString;

        TestMongoSerialization.Configure();
    }

    [OneTimeTearDown]
    public async Task TearDown() => await _mongoReplicaSet.DisposeAsync();
}