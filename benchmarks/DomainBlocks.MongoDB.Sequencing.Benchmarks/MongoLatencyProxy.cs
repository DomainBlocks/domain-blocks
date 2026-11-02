using DomainBlocks.Testing.Integration.MongoDB;
using MongoDB.Driver;
using Testcontainers.MongoDb;
using Testcontainers.Toxiproxy;
using Toxiproxy.Net;
using Toxiproxy.Net.Toxics;

namespace DomainBlocks.MongoDB.Sequencing.Benchmarks;

/// <summary>
/// A Toxiproxy container in front of the replica set that delays what the server sends back to the client.
/// </summary>
internal sealed class MongoLatencyProxy : IAsyncDisposable
{
    private const int ProxyPort = ToxiproxyBuilder.FirstProxiedPort;

    private readonly ToxiproxyContainer _container;
    private readonly Connection _connection;

    private MongoLatencyProxy(
        ToxiproxyContainer container,
        Connection connection,
        string connectionString,
        string description)
    {
        _container = container;
        _connection = connection;
        ConnectionString = connectionString;
        Description = description;
    }

    public string ConnectionString { get; }

    public string Description { get; }

    public static async Task<MongoLatencyProxy> StartAsync(int latencyMillis, int jitterMillis)
    {
        var container = new ToxiproxyBuilder("ghcr.io/shopify/toxiproxy:2.12.0")
            .WithNetwork(MongoTestEnvironment.ReplicaSet.Network)
            .Build();

        await container.StartAsync();

        var connection = new Connection(container.Hostname, container.GetMappedPublicPort());

        // The network alias resolves from inside the Toxiproxy container because both containers are on the same
        // Docker network.
        var proxy = await connection.Client().AddAsync(new Proxy
        {
            Name = "mongo",
            Listen = $"0.0.0.0:{ProxyPort}",
            Upstream = $"{MongoReplicaSet.NetworkAlias}:{MongoDbBuilder.MongoDbPort}",
            Enabled = true
        });

        await proxy.AddAsync(new LatencyToxic
        {
            Name = "latency",
            Stream = ToxicDirection.DownStream,
            Attributes = new LatencyToxic.ToxicAttributes { Latency = latencyMillis, Jitter = jitterMillis }
        });

        var connectionString = new MongoUrlBuilder(MongoTestEnvironment.ConnectionString)
        {
            Server = new MongoServerAddress(container.Hostname, container.GetMappedPublicPort(ProxyPort))
        };

        return new MongoLatencyProxy(
            container,
            connection,
            connectionString.ToMongoUrl().ToString(),
            $"{latencyMillis} ms ± {jitterMillis} ms");
    }

    public async ValueTask DisposeAsync()
    {
        _connection.Dispose();
        await _container.DisposeAsync();
    }
}