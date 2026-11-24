using System.Globalization;
using DotNet.Testcontainers.Networks;
using Testcontainers.Toxiproxy;
using Toxiproxy.Net;
using Toxiproxy.Net.Toxics;

namespace DomainBlocks.Benchmarking;

/// <summary>
/// A Toxiproxy container that forwards connections to a server on the same Docker network and delays what the server
/// sends back, as a network between the client and a remote server would.
/// </summary>
public sealed class NetworkLatencyProxy : IAsyncDisposable
{
    /// <summary>
    /// The environment variable that <see cref="StartFromEnvironmentAsync"/> reads the latency from, in milliseconds.
    /// </summary>
    public const string LatencyEnvironmentVariable = "DBX_BENCHMARK_NETWORK_LATENCY_MS";

    private const int ProxyPort = ToxiproxyBuilder.FirstProxiedPort;

    private readonly ToxiproxyContainer _container;
    private readonly Connection _connection;

    private NetworkLatencyProxy(ToxiproxyContainer container, Connection connection, string description)
    {
        _container = container;
        _connection = connection;
        Description = description;
    }

    /// <summary>
    /// The proxy that <see cref="StartFromEnvironmentAsync"/> started for every connection in the run, if any, which
    /// <see cref="BenchmarkReport"/> names in the environment header.
    /// </summary>
    public static NetworkLatencyProxy? RunWide { get; private set; }

    public string Hostname => _container.Hostname;

    public int Port => _container.GetMappedPublicPort(ProxyPort);

    /// <summary>The latency added to each reply, such as "1 ms" or "5 ms ± 3 ms".</summary>
    public string Description { get; }

    /// <summary>
    /// Starts a proxy in front of the server that <paramref name="upstreamAlias"/> names on <paramref name="network"/>.
    /// </summary>
    public static async Task<NetworkLatencyProxy> StartAsync(
        INetwork network,
        string upstreamAlias,
        int upstreamPort,
        int latencyMillis,
        int jitterMillis = 0)
    {
        var container = new ToxiproxyBuilder("ghcr.io/shopify/toxiproxy:2.12.0")
            .WithNetwork(network)
            .Build();

        await container.StartAsync();

        var connection = new Connection(container.Hostname, container.GetMappedPublicPort());

        // The alias resolves from inside the Toxiproxy container because both containers are on the same network.
        var proxy = await connection.Client().AddAsync(new Proxy
        {
            Name = upstreamAlias,
            Listen = $"0.0.0.0:{ProxyPort}",
            Upstream = $"{upstreamAlias}:{upstreamPort}",
            Enabled = true
        });

        await proxy.AddAsync(new LatencyToxic
        {
            Name = "latency",
            Stream = ToxicDirection.DownStream,
            Attributes = new LatencyToxic.ToxicAttributes { Latency = latencyMillis, Jitter = jitterMillis }
        });

        var description = jitterMillis == 0 ? $"{latencyMillis} ms" : $"{latencyMillis} ms ± {jitterMillis} ms";
        return new NetworkLatencyProxy(container, connection, description);
    }

    /// <summary>
    /// Starts a proxy that adds the latency <see cref="LatencyEnvironmentVariable"/> sets, so that a script can run a
    /// whole benchmark project through it, or returns <see langword="null"/> if the variable isn't set. Zero adds no
    /// latency, which measures what the proxy itself costs.
    /// </summary>
    public static async Task<NetworkLatencyProxy?> StartFromEnvironmentAsync(
        INetwork network,
        string upstreamAlias,
        int upstreamPort)
    {
        var value = Environment.GetEnvironmentVariable(LatencyEnvironmentVariable);

        if (string.IsNullOrEmpty(value))
            return null;

        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var latencyMillis))
        {
            throw new InvalidOperationException(
                $"{LatencyEnvironmentVariable} must be a whole number of milliseconds, not '{value}'.");
        }

        RunWide = await StartAsync(network, upstreamAlias, upstreamPort, latencyMillis);
        return RunWide;
    }

    public async ValueTask DisposeAsync()
    {
        if (RunWide == this)
            RunWide = null;

        _connection.Dispose();
        await _container.DisposeAsync();
    }
}