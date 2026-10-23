using System.Net;
using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Npgsql;
using Testcontainers.PostgreSql;

namespace DomainBlocks.Testing.Integration.EventStore.PostgreSQL;

/// <summary>
/// A throw-away primary with a synchronous standby that streams its WAL through a physical slot. The primary's failover
/// slots wait for the standby, which keeps copies of them. The standby connects as a replication role, so the primary
/// can cut it off, and let it back in, with SQL alone. A proxy reaches whichever server is the primary.
/// </summary>
public sealed class ReplicatedPostgresServer : IAsyncDisposable
{
    /// <summary>
    /// The standby's name, which the primary's <c>synchronous_standby_names</c> lists, and the name of its physical
    /// slot, which the primary's <c>synchronized_standby_slots</c> lists.
    /// </summary>
    public const string StandbyName = "standby";

    private const string ReplicationRole = "replicator";

    // These run once, when the primary is initialized: the replication role, and a rule that lets it connect from the
    // standby's container.
    private const string CreateRoleScript =
        $"CREATE ROLE {ReplicationRole} REPLICATION LOGIN PASSWORD '{ReplicationRole}';";

    private const string AllowReplicationScript =
        $"echo 'host replication {ReplicationRole} all scram-sha-256' >> \"$PGDATA/pg_hba.conf\"";

    // Copies the primary, creating the standby's physical slot, and then runs the copy as a standby. The standby's
    // cluster name is its application name, and it tries again soon after its connection is refused. Copying failover
    // slots needs the database in its connection settings, hot_standby_feedback, and wal_level = logical.
    private const string StandbyScript =
        $"""
         set -e
         gosu postgres pg_basebackup -d "host=primary user={ReplicationRole} dbname=postgres" -D /tmp/standby \
             -R -X stream -c fast -C -S {StandbyName}
         exec gosu postgres postgres -D /tmp/standby -c cluster_name={StandbyName} \
             -c wal_retrieve_retry_interval=100ms -c sync_replication_slots=on -c hot_standby_feedback=on \
             -c wal_level=logical
         """;

    private readonly INetwork _network;
    private readonly PostgreSqlContainer _primary;
    private readonly IContainer _standby;
    private readonly TcpProxy _proxy;

    private ReplicatedPostgresServer(INetwork network, PostgreSqlContainer primary, IContainer standby)
    {
        _network = network;
        _primary = primary;
        _standby = standby;
        _proxy = new TcpProxy(new DnsEndPoint(primary.Hostname, primary.GetMappedPublicPort(5432)));
    }

    public string PrimaryConnectionString => _primary.GetConnectionString();

    public string StandbyConnectionString => new NpgsqlConnectionStringBuilder(PrimaryConnectionString)
    {
        Host = _standby.Hostname,
        Port = _standby.GetMappedPublicPort(5432)
    }.ConnectionString;

    /// <summary>
    /// Reaches the primary through the proxy, which <see cref="PromoteStandbyAsync"/> moves to the standby, as a
    /// failover manager moves a virtual IP.
    /// </summary>
    public string FailoverConnectionString => new NpgsqlConnectionStringBuilder(PrimaryConnectionString)
    {
        Host = IPAddress.Loopback.ToString(),
        Port = _proxy.Port
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
            .WithCommand("-c", "wal_level=logical")
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

        var server = new ReplicatedPostgresServer(network, primary, standby);
        await server.SetOnPrimaryAsync("synchronous_standby_names", StandbyName);
        await server.SetOnPrimaryAsync("synchronized_standby_slots", StandbyName);

        return server;
    }

    /// <summary>
    /// Refuses the standby's connections and ends the one it has, so that it receives no more WAL.
    /// </summary>
    public async Task CutOffStandbyAsync()
    {
        await ExecuteOnPrimaryAsync($"ALTER ROLE {ReplicationRole} NOLOGIN");
        await ExecuteOnPrimaryAsync(
            $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE usename = '{ReplicationRole}'");

        await WaitUntilAsync($"SELECT NOT EXISTS (SELECT 1 FROM pg_stat_activity WHERE usename = '{ReplicationRole}')");
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
    /// Empties the primary's <c>synchronized_standby_slots</c>, so that its failover slots no longer wait for the
    /// standby.
    /// </summary>
    public Task ClearSynchronizedStandbySlotsAsync() => SetOnPrimaryAsync("synchronized_standby_slots", string.Empty);

    /// <summary>
    /// Stops the primary at once, as a crash would. Unlike a clean shutdown, which first sends the rest of the WAL to a
    /// connected standby, this sends the standby nothing more.
    /// </summary>
    public async Task CrashPrimaryAsync()
    {
        var result = await _primary.ExecAsync(
            ["bash", "-c", "gosu postgres pg_ctl stop -m immediate -W -D \"$PGDATA\""]);

        if (result.ExitCode != 0)
            throw new InvalidOperationException($"The primary did not stop: {result.Stderr}");

        // pg_ctl returns once it has signaled the server, so this waits until the server refuses connections.
        while (await AcceptsConnectionsAsync(PrimaryConnectionString))
            await Task.Delay(20);
    }

    /// <summary>
    /// Promotes the standby, which then has every event that it received from the primary, and moves the proxy to it.
    /// </summary>
    public async Task PromoteStandbyAsync()
    {
        await using (var dataSource = NpgsqlDataSource.Create(StandbyConnectionString))
        {
            await using var command = dataSource.CreateCommand("SELECT pg_promote()");
            await command.ExecuteNonQueryAsync();
        }

        _proxy.MoveTo(new DnsEndPoint(_standby.Hostname, _standby.GetMappedPublicPort(5432)));
    }

    /// <summary>
    /// Stops the standby applying WAL, and waits until it has stopped. It still receives and flushes the WAL, so a
    /// commit that waits for the standby to flush it completes, but one that waits for it to apply it, with
    /// <c>remote_apply</c>, waits until replay resumes.
    /// </summary>
    public async Task PauseWalReplayOnStandbyAsync()
    {
        await ExecuteOnStandbyAsync("SELECT pg_wal_replay_pause()");
        await WaitUntilAsync(StandbyConnectionString, "SELECT pg_get_wal_replay_pause_state() = 'paused'");
    }

    public Task ResumeWalReplayOnStandbyAsync() => ExecuteOnStandbyAsync("SELECT pg_wal_replay_resume()");

    public async ValueTask DisposeAsync()
    {
        await _proxy.DisposeAsync();
        await _standby.DisposeAsync();
        await _primary.DisposeAsync();
        await _network.DisposeAsync();
    }

    private static async Task<bool> AcceptsConnectionsAsync(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false, Timeout = 1 };

        try
        {
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            return true;
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException)
        {
            return false;
        }
    }

    // pg_reload_conf only signals the server, so this waits until a session has the setting. Sessions started after
    // that have it too. The standby was copied before this, so a promoted standby doesn't have the setting.
    private async Task SetOnPrimaryAsync(string name, string value)
    {
        await using (var dataSource = NpgsqlDataSource.Create(PrimaryConnectionString))
        {
            await using var alter = dataSource.CreateCommand($"ALTER SYSTEM SET {name} = '{value}'");
            await alter.ExecuteNonQueryAsync();

            await using var reload = dataSource.CreateCommand("SELECT pg_reload_conf()");
            await reload.ExecuteNonQueryAsync();
        }

        await WaitUntilAsync($"SELECT current_setting('{name}') = '{value}'");
    }

    private async Task ExecuteOnStandbyAsync(string sql)
    {
        await using var dataSource = NpgsqlDataSource.Create(StandbyConnectionString);
        await using var command = dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    // The commit doesn't wait for the standby, which may be cut off.
    private async Task ExecuteOnPrimaryAsync(string sql)
    {
        await using var dataSource = NpgsqlDataSource.Create(PrimaryConnectionString);
        await using var command = dataSource.CreateCommand($"SET synchronous_commit = local; {sql}");
        await command.ExecuteNonQueryAsync();
    }

    private Task WaitUntilAsync(string condition) => WaitUntilAsync(PrimaryConnectionString, condition);

    private static async Task WaitUntilAsync(string connectionString, string condition)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var command = dataSource.CreateCommand(condition);

        while (!(bool)(await command.ExecuteScalarAsync())!)
            await Task.Delay(20);
    }
}