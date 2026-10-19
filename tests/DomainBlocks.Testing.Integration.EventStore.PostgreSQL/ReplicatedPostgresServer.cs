using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Npgsql;
using Testcontainers.PostgreSql;

namespace DomainBlocks.Testing.Integration.EventStore.PostgreSQL;

/// <summary>
/// A throw-away primary with a standby that streams its WAL. The standby connects as a replication role, so the primary
/// can cut it off, and let it back in, with SQL alone.
/// </summary>
public sealed class ReplicatedPostgresServer : IAsyncDisposable
{
    /// <summary>
    /// The standby's name, for <c>synchronous_standby_names</c>.
    /// </summary>
    public const string StandbyName = "standby";

    private const string ReplicationRole = "replicator";

    // These run once, when the primary is initialized: the replication role, and a rule that lets it connect from the
    // standby's container.
    private const string CreateRoleScript =
        $"CREATE ROLE {ReplicationRole} REPLICATION LOGIN PASSWORD '{ReplicationRole}';";

    private const string AllowReplicationScript =
        $"echo 'host replication {ReplicationRole} all scram-sha-256' >> \"$PGDATA/pg_hba.conf\"";

    // Copies the primary, and then runs it as a standby. The standby's cluster name is its application name, and it
    // tries again soon after its connection is refused.
    private const string StandbyScript =
        $"""
         set -e
         gosu postgres pg_basebackup -h primary -U {ReplicationRole} -D /tmp/standby -R -X stream -c fast
         exec gosu postgres postgres -D /tmp/standby -c cluster_name={StandbyName} -c wal_retrieve_retry_interval=100ms
         """;

    private readonly INetwork _network;
    private readonly PostgreSqlContainer _primary;
    private readonly IContainer _standby;

    private ReplicatedPostgresServer(INetwork network, PostgreSqlContainer primary, IContainer standby)
    {
        _network = network;
        _primary = primary;
        _standby = standby;
    }

    public string PrimaryConnectionString => _primary.GetConnectionString();

    public string StandbyConnectionString => new NpgsqlConnectionStringBuilder(PrimaryConnectionString)
    {
        Host = _standby.Hostname,
        Port = _standby.GetMappedPublicPort(5432)
    }.ConnectionString;

    public static async Task<ReplicatedPostgresServer> StartAsync()
    {
        var image = Environment.GetEnvironmentVariable(PostgresServer.ImageEnvironmentVariable);

        if (string.IsNullOrEmpty(image))
            image = PostgresServer.DefaultImage;

        var network = new NetworkBuilder().Build();

        var primary = new PostgreSqlBuilder(image)
            .WithNetwork(network)
            .WithNetworkAliases("primary")
            .WithResourceMapping(Encoding.UTF8.GetBytes(CreateRoleScript), "/docker-entrypoint-initdb.d/1-role.sql")
            .WithResourceMapping(Encoding.UTF8.GetBytes(AllowReplicationScript), "/docker-entrypoint-initdb.d/2-hba.sh")
            .Build();

        var standby = new ContainerBuilder(image)
            .WithNetwork(network)
            .WithEnvironment("PGPASSWORD", ReplicationRole)
            .WithEntrypoint("bash", "-c", StandbyScript.ReplaceLineEndings("\n"))
            .WithPortBinding(5432, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("started streaming WAL"))
            .Build();

        await network.CreateAsync();
        await primary.StartAsync();
        await standby.StartAsync();

        return new ReplicatedPostgresServer(network, primary, standby);
    }

    /// <summary>
    /// Refuses the standby's connections and ends the one it has, so that it receives no more WAL.
    /// </summary>
    public async Task CutOffStandbyAsync()
    {
        await ExecuteOnPrimaryAsync($"ALTER ROLE {ReplicationRole} NOLOGIN");
        await ExecuteOnPrimaryAsync("SELECT pg_terminate_backend(pid) FROM pg_stat_replication");
        await WaitUntilAsync("SELECT NOT EXISTS (SELECT 1 FROM pg_stat_replication)");
    }

    /// <summary>
    /// Lets the standby connect again, and waits until it streams.
    /// </summary>
    public async Task ReconnectStandbyAsync()
    {
        await ExecuteOnPrimaryAsync($"ALTER ROLE {ReplicationRole} LOGIN");
        await WaitUntilAsync("SELECT EXISTS (SELECT 1 FROM pg_stat_replication WHERE state = 'streaming')");
    }

    /// <summary>
    /// Promotes the standby, which then has every event that it received before it was cut off.
    /// </summary>
    public async Task PromoteStandbyAsync()
    {
        await using var dataSource = NpgsqlDataSource.Create(StandbyConnectionString);
        await using var command = dataSource.CreateCommand("SELECT pg_promote()");
        await command.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _standby.DisposeAsync();
        await _primary.DisposeAsync();
        await _network.DisposeAsync();
    }

    // The commit doesn't wait for the standby, which may be cut off.
    private async Task ExecuteOnPrimaryAsync(string sql)
    {
        await using var dataSource = NpgsqlDataSource.Create(PrimaryConnectionString);
        await using var command = dataSource.CreateCommand($"SET synchronous_commit = local; {sql}");
        await command.ExecuteNonQueryAsync();
    }

    private async Task WaitUntilAsync(string condition)
    {
        await using var dataSource = NpgsqlDataSource.Create(PrimaryConnectionString);
        await using var command = dataSource.CreateCommand(condition);

        while (!(bool)(await command.ExecuteScalarAsync())!)
            await Task.Delay(20);
    }
}