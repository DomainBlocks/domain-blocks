using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Networks;
using Testcontainers.PostgreSql;

namespace DomainBlocks.Testing.Integration.EventStore.PostgreSQL;

/// <summary>
/// A throw-away PostgreSQL server configured for logical replication. The image can be overridden with the
/// DBX_POSTGRES_IMAGE environment variable, e.g., to run the suite against the minimum supported version.
/// </summary>
public sealed class PostgresServer : IAsyncDisposable
{
    public const string DefaultImage = "postgres:17";
    public const string ImageEnvironmentVariable = "DBX_POSTGRES_IMAGE";

    /// <summary>
    /// The name that resolves to the server's container from other containers on <see cref="Network"/>.
    /// </summary>
    public const string NetworkAlias = "postgres";

    private readonly PostgreSqlContainer _container;

    private PostgresServer(INetwork network, PostgreSqlContainer container)
    {
        Network = network;
        _container = container;
    }

    public string ConnectionString => _container.GetConnectionString();

    public INetwork Network { get; }

    public static async Task<PostgresServer> StartAsync()
    {
        var image = Environment.GetEnvironmentVariable(ImageEnvironmentVariable) is { Length: > 0 } configured
            ? configured
            : DefaultImage;

        var network = new NetworkBuilder()
            .WithName($"postgres-test-{Guid.NewGuid():N}")
            .Build();

        await network.CreateAsync();

        var container = new PostgreSqlBuilder(image)
            .WithCommand("-c", "wal_level=logical", "-c", "max_replication_slots=16", "-c", "max_wal_senders=16")
            .WithNetwork(network)
            .WithNetworkAliases(NetworkAlias)
            .Build();

        await container.StartAsync();

        return new PostgresServer(network, container);
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
        await Network.DisposeAsync();
    }
}