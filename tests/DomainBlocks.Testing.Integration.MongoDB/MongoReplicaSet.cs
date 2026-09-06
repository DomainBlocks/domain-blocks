using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Networks;
using Testcontainers.MongoDb;

namespace DomainBlocks.Testing.Integration.MongoDB;

public sealed class MongoReplicaSet : IAsyncDisposable
{
    private readonly INetwork _network;
    private readonly MongoDbContainer _container;

    private MongoReplicaSet(INetwork network, MongoDbContainer container)
    {
        _network = network;
        _container = container;
    }

    public string ConnectionString => _container.GetConnectionString();

    public static async Task<MongoReplicaSet> CreateAsync(string networkAlias = "mongo")
    {
        var network = new NetworkBuilder()
            .WithName($"mongo-test-{Guid.NewGuid():N}")
            .Build();

        await network.CreateAsync();

        var container = new MongoDbBuilder("mongo:7.0")
            .WithReplicaSet()
            .WithNetwork(network)
            .WithNetworkAliases(networkAlias)
            .Build();

        await container.StartAsync();

        return new MongoReplicaSet(network, container);
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
        await _network.DisposeAsync();
    }
}