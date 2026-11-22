using DomainBlocks.Testing.Integration.MongoDB;
using MongoDB.Driver;
using Testcontainers.MongoDb;
using Testcontainers.Toxiproxy;
using Toxiproxy.Net;
using Toxiproxy.Net.Toxics;

namespace DomainBlocks.EventStore.Tests.Chaos;

/// <summary>
/// Represents a Toxiproxy container in front of a replica set, which can lose the replies that the server sends back.
/// </summary>
internal sealed class MongoFaultProxy : IAsyncDisposable
{
    private const string ReplyLossToxicName = "reply-loss";
    private const int ProxyPort = ToxiproxyBuilder.FirstProxiedPort;

    private readonly ToxiproxyContainer _container;
    private readonly Connection _connection;
    private readonly Proxy _proxy;

    private MongoFaultProxy(ToxiproxyContainer container, Connection connection, Proxy proxy, string connectionString)
    {
        _container = container;
        _connection = connection;
        _proxy = proxy;
        ConnectionString = connectionString;
    }

    /// <summary>
    /// Gets the connection string that reaches the replica set through the proxy.
    /// </summary>
    public string ConnectionString { get; }

    public static async Task<MongoFaultProxy> StartAsync(
        MongoReplicaSet replicaSet,
        CancellationToken cancellationToken)
    {
        var container = new ToxiproxyBuilder("ghcr.io/shopify/toxiproxy:2.12.0")
            .WithNetwork(replicaSet.Network)
            .Build();

        await container.StartAsync(cancellationToken);

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

        // A direct connection keeps the driver on the proxy. Otherwise it would discover the member's own address and
        // connect to that.
        var url = new MongoUrlBuilder(replicaSet.ConnectionString)
        {
            Server = new MongoServerAddress(container.Hostname, container.GetMappedPublicPort(ProxyPort)),
            DirectConnection = true
        };

        return new MongoFaultProxy(container, connection, proxy, url.ToMongoUrl().ToString());
    }

    /// <summary>
    /// Starts losing everything that the server sends back, while requests still reach it. The server may carry out a
    /// request without the client ever learning of it.
    /// </summary>
    public async Task StartLosingRepliesAsync()
    {
        await _proxy.AddAsync(new TimeoutToxic
        {
            Name = ReplyLossToxicName,
            Stream = ToxicDirection.DownStream,
            Attributes = new TimeoutToxic.ToxicAttributes { Timeout = 0 }
        });
    }

    /// <summary>
    /// Stops losing replies, and closes every connection through the proxy, since a connection that lost part of a
    /// reply can't be used again.
    /// </summary>
    public async Task StopLosingRepliesAsync()
    {
        // Disabling the proxy closes its connections.
        _proxy.Enabled = false;
        await _proxy.UpdateAsync();
        await _proxy.RemoveToxicAsync(ReplyLossToxicName);
        _proxy.Enabled = true;
        await _proxy.UpdateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _connection.Dispose();
        await _container.DisposeAsync();
    }
}