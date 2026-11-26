using DomainBlocks.Benchmarking;
using DomainBlocks.Testing.Integration.MongoDB;
using NUnit.Framework;
using Testcontainers.MongoDb;

namespace DomainBlocks.MongoDB.Sequencing.Benchmarks;

[SetUpFixture]
public class SetUpFixture
{
    private NetworkLatencyProxy? _proxy;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        await MongoTestEnvironment.StartAsync();

        _proxy = await NetworkLatencyProxy.StartFromEnvironmentAsync(
            MongoTestEnvironment.ReplicaSet.Network,
            MongoReplicaSet.NetworkAlias,
            MongoDbBuilder.MongoDbPort);

        if (_proxy is not null)
            MongoTestEnvironment.UseProxy(_proxy.Hostname, _proxy.Port);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        // The proxy is on the replica set's network, which can only be removed once nothing is attached to it.
        if (_proxy is not null)
            await _proxy.DisposeAsync();

        await MongoTestEnvironment.StopAsync();
    }
}