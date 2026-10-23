using System.Security.Cryptography;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing;
using DomainBlocks.Testing.Events;
using DomainBlocks.Testing.Integration.EventStore.PostgreSQL;
using Microsoft.Extensions.Logging;
using Npgsql;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Aurora;

using EventStore = IEventStore<object, string, StreamPosition, LogPosition>;
using Subscription = IAsyncEnumerator<SubscriptionMessage<object, string, StreamPosition, LogPosition>>;

/// <summary>
/// Live subscriptions on an Aurora PostgreSQL cluster with a writer and a reader, which <c>run.sh</c> creates. See
/// README.md.
/// </summary>
[TestFixture]
[Explicit("Needs an Aurora cluster. See README.md.")]
[NonParallelizable]
public class AuroraSubscriptionTests
{
    private const int TimeoutMillis = 2 * 60 * 1000;
    private const int FailoverTimeoutMillis = 10 * 60 * 1000;
    private const string RoleName = "dbx_app";

    private static readonly TimeSpan ReaderDuration = TimeSpan.FromSeconds(10);
    private static readonly EventTypeMap EventTypeMap = new EventTypeMapBuilder().Add<TestEvent>().Build();

    // The feed reconnects quickly, so the tests don't wait long once the writer is reachable again.
    private readonly PostgresEventStoreOptions _options = new()
    {
        Schema = "dbx_aurora",
        Replication =
        {
            RetryDelay = TimeSpan.FromMilliseconds(100),
            MaxRetryDelay = TimeSpan.FromSeconds(1)
        }
    };

    private AuroraCluster _cluster = null!;
    private ILoggerFactory _loggerFactory = null!;
    private string _connectionString = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _cluster = AuroraCluster.FromEnvironment();
        _loggerFactory = TestLoggerFactory.Create();

        // The store runs as an ordinary role with the replication grant, as an application would.
        var masterConnectionString = await _cluster.GetMasterConnectionStringAsync(CancellationToken.None);
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

        await using var dataSource = NpgsqlDataSource.Create(masterConnectionString);

        await using var command = dataSource.CreateCommand(
            $"""
             DO $$
             BEGIN
                 IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '{RoleName}') THEN
                     CREATE ROLE {RoleName} LOGIN;
                 END IF;
             END $$;
             ALTER ROLE {RoleName} PASSWORD '{password}';
             GRANT rds_replication TO {RoleName};
             GRANT CREATE ON DATABASE postgres TO {RoleName};
             """);

        await command.ExecuteNonQueryAsync();

        // The feed's replication connection uses the data source's connection string, so it keeps the password.
        _connectionString = new NpgsqlConnectionStringBuilder(masterConnectionString)
        {
            Username = RoleName,
            Password = password,
            PersistSecurityInfo = true
        }.ConnectionString;
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _loggerFactory.Dispose();
        _cluster.Dispose();
    }

    [SetUp]
    public async Task SetUp()
    {
        await using var dataSource = CreateDataSource(_connectionString);
        await PostgresEventStoreAdmin.DropAsync(dataSource, _options);
        await PostgresEventStoreAdmin.EnsureInitializedAsync(dataSource, _options);
    }

    [Test]
    [CancelAfter(FailoverTimeoutMillis)]
    public async Task FailOver_PlainSlot_KeepsSlotAndChanges(CancellationToken ct)
    {
        const string slotName = "dbx_aurora_plain";

        const string dropSlotSql =
            $"SELECT pg_drop_replication_slot(slot_name) FROM pg_replication_slots WHERE slot_name = '{slotName}'";

        await using var dataSource = CreateDataSource(_connectionString);
        await using var eventStore = CreateEventStore(dataSource);

        // PostgreSQL copies only failover slots to its standbys, so a plain slot that survives a failover survives in
        // Aurora's shared storage.
        await using (var drop = dataSource.CreateCommand(dropSlotSql))
            await drop.ExecuteNonQueryAsync(ct);

        await using (var create = dataSource.CreateCommand(
                         $"SELECT pg_create_logical_replication_slot('{slotName}', 'pgoutput', failover => false)"))
        {
            await create.ExecuteNonQueryAsync(ct);
        }

        await AppendAsync(eventStore, "e", ct);
        await _cluster.FailOverAsync(ct);

        // The writer's own endpoint, since the cluster endpoint's name can still resolve to the old writer.
        var (writer, _) = await _cluster.GetInstanceEndpointsAsync(ct);

        var writerConnectionString = new NpgsqlConnectionStringBuilder(_connectionString)
        {
            Host = writer.Host,
            Port = writer.Port
        };

        await using var writerDataSource = NpgsqlDataSource.Create(writerConnectionString.ConnectionString);

        await using var check = writerDataSource.CreateCommand(
            $"""
             SELECT pg_is_in_recovery(),
                    failover,
                    (SELECT count(*)
                     FROM pg_logical_slot_peek_binary_changes(
                         slot_name, NULL, NULL,
                         'proto_version', '1',
                         'publication_names', (SELECT pubname
                                               FROM pg_publication_tables
                                               WHERE schemaname = '{_options.Schema}')))
             FROM pg_replication_slots
             WHERE slot_name = '{slotName}'
             """);

        try
        {
            await using var reader = await check.ExecuteReaderAsync(ct);

            (await reader.ReadAsync(ct)).ShouldBeTrue("The slot is gone.");
            reader.GetBoolean(0).ShouldBeFalse("The endpoint reached a reader.");
            reader.GetBoolean(1).ShouldBeFalse();
            reader.GetInt64(2).ShouldBeGreaterThan(0, "The slot lost the event's change.");
        }
        finally
        {
            await using var drop = writerDataSource.CreateCommand(dropSlotSql);
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    [Test]
    [CancelAfter(TimeoutMillis)]
    public async Task SubscribeToAll_AppendWhileLive_DeliversEvent(CancellationToken ct)
    {
        await using var dataSource = CreateDataSource(_connectionString);
        await using var eventStore = CreateEventStore(dataSource);

        await using var subscription = eventStore.SubscribeToAll(SubscriptionOrigin.Start).GetAsyncEnumerator(ct);
        (await subscription.MoveNextAsync()).ShouldBeTrue();
        subscription.Current.Kind.ShouldBe(SubscriptionMessageKind.CaughtUp);

        await AppendAsync(eventStore, "e", ct);

        (await subscription.MoveNextAsync()).ShouldBeTrue();
        subscription.Current.Event.ShouldNotBeNull().Payload.ShouldBe(new TestEvent { Value = "e" });
    }

    [Test]
    [CancelAfter(TimeoutMillis)]
    public async Task SubscribeToAll_EndpointReachesReader_DeliversOnceEndpointReachesWriter(CancellationToken ct)
    {
        var (writer, reader) = await _cluster.GetInstanceEndpointsAsync(ct);

        // The proxy stands in for a cluster endpoint whose name still resolves to the old writer after a failover.
        await using var proxy = new TcpProxy(reader);

        var proxied = new NpgsqlConnectionStringBuilder(_connectionString) { Host = "127.0.0.1", Port = proxy.Port };
        await using var proxiedDataSource = CreateDataSource(proxied.ConnectionString);
        await using var eventStore = CreateEventStore(proxiedDataSource);

        await using var subscription = eventStore.SubscribeToAll(SubscriptionOrigin.Start).GetAsyncEnumerator(ct);
        var next = subscription.MoveNextAsync().AsTask();

        // The subscription keeps trying while it reaches the reader. If it fails instead, this throws its error.
        await Task.WhenAny(next, Task.Delay(ReaderDuration, ct));

        if (next.IsFaulted)
            await next;

        proxy.MoveTo(writer);

        await using var dataSource = CreateDataSource(_connectionString);
        await using var appender = CreateEventStore(dataSource);
        await AppendAsync(appender, "e", ct);

        (await next).ShouldBeTrue();

        while (subscription.Current.Event is not { Payload: TestEvent { Value: "e" } })
            (await subscription.MoveNextAsync()).ShouldBeTrue();
    }

    [Test]
    [CancelAfter(FailoverTimeoutMillis)]
    public async Task SubscribeToAll_WriterFailover_DeliversEveryEventOnce(CancellationToken ct)
    {
        await using var dataSource = CreateDataSource(_connectionString);
        await using var eventStore = CreateEventStore(dataSource);

        await using var subscription = eventStore.SubscribeToAll(SubscriptionOrigin.Start).GetAsyncEnumerator(ct);
        var delivered = ReadUntilAsync(subscription, "end");

        var appender = new ContinuousAppender(eventStore);
        using var stop = new CancellationTokenSource();
        var appending = appender.RunAsync(stop.Token);

        await WaitUntilAsync(() => appender.Count >= 20, ct);
        await _cluster.FailOverAsync(ct);

        var appendedBeforeFailoverEnded = appender.Count;
        await WaitUntilAsync(() => appender.Count >= appendedBeforeFailoverEnded + 20, ct);

        await stop.CancelAsync();
        await appending;
        await AppendAsync(eventStore, "end", ct);

        // An append that failed during the failover may have committed, so the log, not the appender, says which
        // events exist.
        var logged = await eventStore
            .ReadAll()
            .Select(x => (x.Context.LogPosition, ((TestEvent)x.Payload).Value))
            .ToListAsync(ct);

        (await delivered).ShouldBe(logged);
    }

    private static Task AppendAsync(EventStore eventStore, string value, CancellationToken ct)
    {
        return eventStore.AppendAsync(
            "s1",
            [AppendableEvent.Create<object>(new TestEvent { Value = value })],
            cancellationToken: ct);
    }

    // Returns the position and value of every event delivered, up to and including the one with the given value.
    private static async Task<List<(LogPosition, string)>> ReadUntilAsync(Subscription subscription, string last)
    {
        var events = new List<(LogPosition, string)>();

        while (await subscription.MoveNextAsync())
        {
            if (subscription.Current.Event is not { } e)
                continue;

            var value = ((TestEvent)e.Payload).Value;
            events.Add((e.Context.LogPosition, value));

            if (value == last)
                return events;
        }

        throw new InvalidOperationException("The subscription ended.");
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken ct)
    {
        while (!condition())
            await Task.Delay(100, ct);
    }

    private EventStore CreateEventStore(NpgsqlDataSource dataSource)
    {
        return new PostgresEventStoreBuilder<object>()
            .UseDataSource(dataSource)
            .UseOptions(_options)
            .ConfigureCodec(x => x.UseEventTypeMap(EventTypeMap))
            .UseLogger(_loggerFactory.CreateLogger(nameof(AuroraSubscriptionTests)))
            .Build();
    }

    private NpgsqlDataSource CreateDataSource(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.UsePostgresEventStore(_options);
        return builder.Build();
    }

    // Appends one event at a time until stopped, and retries after a failure, as an application would through a
    // failover.
    private sealed class ContinuousAppender(EventStore eventStore)
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public async Task RunAsync(CancellationToken stop)
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    await AppendAsync(eventStore, $"e{Count}", CancellationToken.None);
                    Interlocked.Increment(ref _count);
                }
                catch (Exception)
                {
                    await Task.Delay(100, CancellationToken.None);
                }
            }
        }
    }
}