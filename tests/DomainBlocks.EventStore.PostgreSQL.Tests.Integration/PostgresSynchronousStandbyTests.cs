using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing.Events;
using DomainBlocks.Testing.Integration.EventStore;
using DomainBlocks.Testing.Integration.EventStore.PostgreSQL;
using Npgsql;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration;

using EventStore = IEventStore<object, string, StreamPosition, LogPosition>;

/// <summary>
/// The store with a real synchronous standby, which the primary can cut off from its WAL.
/// </summary>
[TestFixture]
[NonParallelizable]
public class PostgresSynchronousStandbyTests
{
    private static readonly TimeSpan HoldDuration = TimeSpan.FromSeconds(1);
    private static readonly EventTypeMap EventTypeMap = new EventTypeMapBuilder().Add<TestEvent>().Build();

    private readonly PostgresEventStoreOptions _options = new() { Schema = "dbx_standby" };

    private ReplicatedPostgresServer _server = null!;

    [SetUp]
    public async Task SetUp()
    {
        _server = await ReplicatedPostgresServer.StartAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _server.DisposeAsync();
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task AppendAsync_SessionWithSynchronousCommitOff_WaitsForStandby(CancellationToken ct)
    {
        await using var dataSource = CreateDataSource(x => x.Options = "-c synchronous_commit=off");
        await PostgresEventStoreAdmin.EnsureInitializedAsync(dataSource, _options, cancellationToken: ct);
        await using var eventStore = CreateEventStore(dataSource);

        await _server.CutOffStandbyAsync();

        var append = eventStore.AppendAsync(
            "s1",
            [AppendableEvent.Create<object>(new TestEvent { Value = "e" })],
            cancellationToken: ct);

        var pid = await WaitForSyncRepAsync(ct);

        // A commit whose wait ended soon after it began would have completed during the hold.
        await Task.Delay(HoldDuration, ct);
        (await IsWaitingForSyncRepAsync(pid, ct)).ShouldBeTrue();
        append.IsCompleted.ShouldBeFalse();

        await _server.ReconnectStandbyAsync();
        await append;
    }

    // Returns the process ID of a backend whose commit waits for the standby, once there is one.
    private async Task<int> WaitForSyncRepAsync(CancellationToken ct)
    {
        await using var dataSource = CreateDataSource();

        await using var command = dataSource.CreateCommand(
            "SELECT pid FROM pg_stat_activity WHERE wait_event = 'SyncRep' LIMIT 1");

        while (true)
        {
            if (await command.ExecuteScalarAsync(ct) is int pid)
                return pid;

            await Task.Delay(20, ct);
        }
    }

    private async Task<bool> IsWaitingForSyncRepAsync(int pid, CancellationToken ct)
    {
        await using var dataSource = CreateDataSource();

        await using var command = dataSource.CreateCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE pid = $1 AND wait_event = 'SyncRep')");

        command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = pid });
        return (bool)(await command.ExecuteScalarAsync(ct)).ShouldNotBeNull();
    }

    private EventStore CreateEventStore(NpgsqlDataSource dataSource)
    {
        return new PostgresEventStoreBuilder<object>()
            .UseDataSource(dataSource)
            .UseOptions(_options)
            .ConfigureCodec(x => x.UseEventTypeMap(EventTypeMap))
            .UseLogger(PostgresTestEnvironment.LoggerFactory.CreateLogger(nameof(PostgresSynchronousStandbyTests)))
            .Build();
    }

    private NpgsqlDataSource CreateDataSource(Action<NpgsqlConnectionStringBuilder>? configure = null)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(_server.PrimaryConnectionString);
        configure?.Invoke(connectionString);

        var builder = new NpgsqlDataSourceBuilder(connectionString.ConnectionString);
        builder.UsePostgresEventStore(_options);
        return builder.Build();
    }
}