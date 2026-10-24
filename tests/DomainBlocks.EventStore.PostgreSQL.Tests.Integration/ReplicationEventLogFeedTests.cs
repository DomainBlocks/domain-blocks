using System.Threading.Channels;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.PostgreSQL.Feeds;
using DomainBlocks.Testing.Integration.EventStore;
using DomainBlocks.Testing.Integration.EventStore.PostgreSQL;
using Npgsql;
using Npgsql.Replication;
using NUnit.Framework;
using Shouldly;
using static DomainBlocks.EventStore.PostgreSQL.Tests.Integration.AppendFunctionClient;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration;

using RawReadEvent = ReadEvent<
    ReplicationEventLogFeedTests.RawEvent,
    string,
    StreamPosition,
    LogPosition>;

/// <summary>
/// Exercises the logical replication session through the feed, against a real server. The event of each row is decoded
/// with a pass-through decoder so that the raw column values can be asserted on.
/// </summary>
[TestFixture]
public class ReplicationEventLogFeedTests : PostgresIntegrationTest
{
    private readonly SlotNameGenerator _slotNames = new("dbx_test");

    private SchemaObjectNames Names => new(Schema);

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task Connect_WhenRowsAreInserted_NotifiesAttachedObserversInOrder(CancellationToken ct)
    {
        var feed = CreateFeed();
        var observer1 = new CollectingObserver();
        var observer2 = new CollectingObserver();
        using var attachment1 = feed.Attach(observer1);
        using var attachment2 = feed.Attach(observer2);
        await using var connection = await feed.ConnectAsync(ct);

        var events = Enumerable.Range(0, 10).Select(i => JsonEvent($"e{i}", $"{{\"i\": {i}}}")).ToArray();
        await Client.AppendAsync([Any("s1", events[..6]), Any("s2", events[6..])], ct);

        var events1 = await observer1.ReadAsync(10, ct);
        var events2 = await observer2.ReadAsync(10, ct);

        events1
            .Select(x => x.Context.LogPosition)
            .ShouldBe(Enumerable.Range(0, 10).Select(i => LogPosition.FromInt64(i)));

        events1.Select(x => x.Payload.EventName).ShouldBe(events.Select(x => x.Name));

        events1
            .Select(x => (x.Context.StreamId, x.Context.StreamPosition))
            .ShouldBe(Enumerable.Range(0, 6)
                .Select(i => ("s1", StreamPosition.FromInt64(i)))
                .Concat(Enumerable.Range(0, 4).Select(i => ("s2", StreamPosition.FromInt64(i)))));

        events2.Select(x => x.Context.LogPosition).ShouldBe(events1.Select(x => x.Context.LogPosition));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task Connect_OnlyDeliversRowsCommittedAfterConnect(CancellationToken ct)
    {
        await Client.AppendAsync([Any("s1", JsonEvent("before"))], ct);

        var feed = CreateFeed();
        var observer = new CollectingObserver();
        using var attachment = feed.Attach(observer);
        await using var connection = await feed.ConnectAsync(ct);

        await Client.AppendAsync([Any("s1", JsonEvent("after"))], ct);

        var events = await observer.ReadAsync(1, ct);
        events[0].Payload.EventName.ShouldBe("after");
        events[0].Context.LogPosition.ShouldBe(LogPosition.FromInt64(1));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task Connect_DecodesAllColumns(CancellationToken ct)
    {
        var feed = CreateFeed();
        var observer = new CollectingObserver();
        using var attachment = feed.Attach(observer);
        await using var connection = await feed.ConnectAsync(ct);

        byte[] bytes = [1, 2, 3, 255];
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        await Client.AppendAsync(
            [
                Any(
                    "stream-1",
                    Event.WithJson("json-event", "{\"a\": 1}", "{\"tenant\": \"x\"}"),
                    Event.WithBytes("bytes-event", bytes))
            ],
            ct);

        var events = await observer.ReadAsync(2, ct);

        events[0].Context.StreamId.ShouldBe("stream-1");
        events[0].Context.StreamPosition.ShouldBe(StreamPosition.FromInt64(0));
        events[0].Payload.EventName.ShouldBe("json-event");
        events[0].Payload.EventData.IsJson.ShouldBeTrue();
        events[0].Payload.EventData.Json.ShouldBe("{\"a\": 1}");
        events[0].Payload.Metadata.ShouldBe("{\"tenant\": \"x\"}");
        events[0].Context.CreatedAt.Offset.ShouldBe(TimeSpan.Zero);
        events[0].Context.CreatedAt.ShouldBeInRange(before, DateTimeOffset.UtcNow.AddSeconds(1));

        events[1].Context.StreamPosition.ShouldBe(StreamPosition.FromInt64(1));
        events[1].Payload.EventName.ShouldBe("bytes-event");
        events[1].Payload.EventData.IsBytes.ShouldBeTrue();
        events[1].Payload.EventData.Bytes.ToArray().ShouldBe(bytes);
        events[1].Payload.Metadata.ShouldBeNull();
        events[1].Context.CreatedAt.ShouldBe(events[0].Context.CreatedAt);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task Connect_WhenWalSenderIsTerminated_ResumesWithoutRepeatingRows(CancellationToken ct)
    {
        var feed = CreateFeed();
        var observer = new CollectingObserver();
        using var attachment = feed.Attach(observer);
        await using var connection = await feed.ConnectAsync(ct);

        await Client.AppendAsync([Any("s1", JsonEvent("e0"), JsonEvent("e1"))], ct);
        await Client.AppendAsync([Any("s1", JsonEvent("e2"))], ct);
        (await observer.ReadAsync(3, ct)).Select(x => x.Payload.EventName).ShouldBe(["e0", "e1", "e2"]);

        // The feed hasn't yet reported these rows to the server, so its slot would start before them.
        await TerminateWalSenderAsync(ct);
        await Client.AppendAsync([Any("s1", JsonEvent("e3"))], ct);

        (await observer.ReadAsync(1, ct)).Single().Payload.EventName.ShouldBe("e3");
        observer.ResetCount.ShouldBe(0);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task Connect_CreatesPermanentSlotAndDisposeDropsIt(CancellationToken ct)
    {
        var feed = CreateFeed();
        var observer = new CollectingObserver();
        using var attachment = feed.Attach(observer);
        var connection = await feed.ConnectAsync(ct);

        // The feed connects before it starts streaming, and the slot is active only once it streams.
        await Client.AppendAsync([Any("s1", JsonEvent("e"))], ct);
        await observer.ReadAsync(1, ct);

        var slots = await GetSlotsAsync(ct);
        var (name, plugin, temporary, active) = slots.ShouldHaveSingleItem();
        temporary.ShouldBeFalse();
        active.ShouldBeTrue();
        plugin.ShouldBe("pgoutput");
        name.ShouldStartWith("dbx_test_");

        await connection.DisposeAsync();

        (await GetSlotsAsync(ct)).ShouldBeEmpty();
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task Connect_SlotsWithPrefix_DropsOnlyInactiveOnesPastTimeout(CancellationToken ct)
    {
        var options = new PostgresReplicationOptions
        {
            SlotNamePrefix = "dbx_cleanup",
            InactiveSlotTimeout = TimeSpan.FromSeconds(1)
        };

        var slotNames = new SlotNameGenerator(options.SlotNamePrefix);

        try
        {
            var running = CreateFeed(slotNames, options);
            using var runningAttachment = running.Attach(new CollectingObserver());
            await using var runningConnection = await running.ConnectAsync(ct);

            await CreateSlotAsync("dbx_cleanup_orphan", ct);
            await CreateSlotAsync("dbx_cleanupx_orphan", ct);
            await Task.Delay(options.InactiveSlotTimeout * 2, ct);
            await CreateSlotAsync("dbx_cleanup_recent", ct);

            var starting = CreateFeed(slotNames, options);
            using var startingAttachment = starting.Attach(new CollectingObserver());
            await using var startingConnection = await starting.ConnectAsync(ct);

            var names = await GetSlotNamesAsync("dbx_cleanup", ct);
            names.ShouldNotContain("dbx_cleanup_orphan");
            names.ShouldContain("dbx_cleanupx_orphan");
            names.ShouldContain("dbx_cleanup_recent");
            names.Count(x => x.StartsWith($"{options.SlotNamePrefix}_", StringComparison.Ordinal)).ShouldBe(3);
        }
        finally
        {
            await DropSlotIfExistsAsync("dbx_cleanupx_orphan");
            await DropSlotIfExistsAsync("dbx_cleanup_recent");
        }
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task Connect_WhenPublicationIsMissing_Faults(CancellationToken ct)
    {
        var options = new PostgresEventStoreOptions { Schema = "dbx_no_publication" };
        var adminOptions = new PostgresEventStoreAdminOptions { CreatePublication = false };
        await PostgresEventStoreAdmin.EnsureInitializedAsync(DataSource, options, adminOptions, ct);

        var feed = new EventLogFeed<EventLogRow<RawEvent>>(
            CreateSessionFactory(_slotNames, new SchemaObjectNames(options.Schema), Options.Replication),
            static row => row.Position,
            new EventLogFeedOptions { RetryDelay = TimeSpan.FromMilliseconds(10), MaxRetryAttempts = 1 },
            LoggerFactory.CreateLogger("EventLogFeed"));

        var observer = new CollectingObserver();
        using var attachment = feed.Attach(observer);

        // The slot is created without error, because pgoutput only resolves publications when it decodes the first
        // change.
        await using var connection = await feed.ConnectAsync(ct);
        await Client.AppendAsync([Any("s1", JsonEvent("trigger"))], ct);

        var ex = await observer.Error.WaitAsync(ct);
        ex.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.UndefinedObject);
        await connection.Completion.WaitAsync(ct).ShouldThrowAsync<PostgresException>();
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task TryTerminateWalSenderAsync_OrdinaryBackend_LeavesItRunning(CancellationToken ct)
    {
        await using var connection = await DataSource.OpenConnectionAsync(ct);

        await ReplicationEventLogSessionFactory<RawEvent>.TryTerminateWalSenderAsync(
            DataSource,
            connection.ProcessID,
            ct);

        await using var command = new NpgsqlCommand("SELECT 1", connection);
        (await command.ExecuteScalarAsync(ct)).ShouldBe(1);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task TryTerminateWalSenderAsync_WalSender_EndsIt(CancellationToken ct)
    {
        const string applicationName = "dbx_test_walsender";

        var connectionString = new NpgsqlConnectionStringBuilder(PostgresTestEnvironment.ConnectionString)
        {
            ApplicationName = applicationName
        }.ConnectionString;

        await using var replication = new LogicalReplicationConnection(connectionString);
        await replication.Open(ct);

        var pid = await GetWalSenderPidAsync(applicationName, ct);
        await ReplicationEventLogSessionFactory<RawEvent>.TryTerminateWalSenderAsync(DataSource, pid, ct);

        (await IsBackendRunningAsync(pid, ct)).ShouldBeFalse();
    }

    private EventLogFeed<EventLogRow<RawEvent>> CreateFeed() => CreateFeed(_slotNames, Options.Replication);

    private EventLogFeed<EventLogRow<RawEvent>> CreateFeed(
        SlotNameGenerator slotNames,
        PostgresReplicationOptions replicationOptions)
    {
        return new EventLogFeed<EventLogRow<RawEvent>>(
            CreateSessionFactory(slotNames, Names, replicationOptions),
            static row => row.Position,
            new EventLogFeedOptions { RetryDelay = TimeSpan.FromMilliseconds(100) },
            LoggerFactory.CreateLogger("EventLogFeed"));
    }

    private ReplicationEventLogSessionFactory<RawEvent> CreateSessionFactory(
        SlotNameGenerator slotNames,
        SchemaObjectNames names,
        PostgresReplicationOptions replicationOptions)
    {
        return new ReplicationEventLogSessionFactory<RawEvent>(
            PostgresTestEnvironment.ConnectionString,
            DataSource,
            slotNames,
            names,
            replicationOptions,
            RawDecoder.Instance,
            LoggerFactory.CreateLogger("ReplicationEventLogSession"));
    }

    private static async Task TerminateWalSenderAsync(CancellationToken ct)
    {
        await using var command = PostgresTestEnvironment.DataSource.CreateCommand(
            "SELECT pg_terminate_backend(active_pid) FROM pg_replication_slots " +
            "WHERE slot_name LIKE 'dbx_test_%' AND active_pid IS NOT NULL");

        (await command.ExecuteScalarAsync(ct)).ShouldBe(true);
    }

    private static async Task CreateSlotAsync(string slotName, CancellationToken ct)
    {
        await using var command = PostgresTestEnvironment.DataSource.CreateCommand(
            "SELECT pg_create_logical_replication_slot($1, 'pgoutput')");

        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = slotName });
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task DropSlotIfExistsAsync(string slotName)
    {
        await using var command = PostgresTestEnvironment.DataSource.CreateCommand(
            "SELECT pg_drop_replication_slot(slot_name) FROM pg_replication_slots WHERE slot_name = $1");

        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = slotName });
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> GetWalSenderPidAsync(string applicationName, CancellationToken ct)
    {
        await using var command = PostgresTestEnvironment.DataSource.CreateCommand(
            "SELECT pid FROM pg_stat_activity WHERE application_name = $1 AND backend_type = 'walsender'");

        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = applicationName });
        return (int)(await command.ExecuteScalarAsync(ct)).ShouldNotBeNull();
    }

    private static async Task<bool> IsBackendRunningAsync(int pid, CancellationToken ct)
    {
        await using var command = PostgresTestEnvironment.DataSource.CreateCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE pid = $1)");

        command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = pid });
        return (bool)(await command.ExecuteScalarAsync(ct)).ShouldNotBeNull();
    }

    private static async Task<List<string>> GetSlotNamesAsync(string prefix, CancellationToken ct)
    {
        await using var command = PostgresTestEnvironment.DataSource.CreateCommand(
            "SELECT slot_name FROM pg_replication_slots WHERE starts_with(slot_name, $1)");

        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = prefix });

        var names = new List<string>();

        await using var reader = await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
            names.Add(reader.GetString(0));

        return names;
    }

    private static async Task<List<(string Name, string Plugin, bool Temporary, bool Active)>> GetSlotsAsync(
        CancellationToken ct)
    {
        await using var command = PostgresTestEnvironment.DataSource.CreateCommand(
            "SELECT slot_name, plugin, temporary, active FROM pg_replication_slots WHERE slot_name LIKE 'dbx_test_%'");

        var slots = new List<(string, string, bool, bool)>();

        await using var reader = await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
            slots.Add((reader.GetString(0), reader.GetString(1), reader.GetBoolean(2), reader.GetBoolean(3)));

        return slots;
    }

    /// <summary>
    /// What the session yields when decoded with <see cref="RawDecoder"/>.
    /// </summary>
    internal sealed record RawEvent(string EventName, PostgresEventData EventData, string? Metadata);

    private sealed class RawDecoder : IEventDecoder<RawEvent, PostgresEventData, string>
    {
        public static readonly RawDecoder Instance = new();

        public DecodedEvent<RawEvent> Decode(string eventName, PostgresEventData eventData, string? metadata)
        {
            return DecodedEvent.Create(
                new RawEvent(eventName, eventData, metadata),
                new Dictionary<string, string>());
        }
    }

    private sealed class CollectingObserver : IEventLogObserver<EventLogRow<RawEvent>>
    {
        private readonly Channel<RawReadEvent> _channel = Channel.CreateUnbounded<RawReadEvent>();

        private readonly TaskCompletionSource<Exception> _errorTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<Exception> Error => _errorTcs.Task;

        public int ResetCount { get; private set; }

        public ValueTask OnNextAsync(EventLogRow<RawEvent> row, CancellationToken cancellationToken)
        {
            // The row is only valid during this call.
            _channel.Writer.TryWrite(ReadEvent.Create(row.Payload, row.Metadata, row.Context));
            return ValueTask.CompletedTask;
        }

        public ValueTask OnResetAsync(CancellationToken cancellationToken)
        {
            ResetCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask OnErrorAsync(Exception exception, CancellationToken cancellationToken)
        {
            _errorTcs.TrySetResult(exception);
            return ValueTask.CompletedTask;
        }

        public async Task<RawReadEvent[]> ReadAsync(int count, CancellationToken cancellationToken)
        {
            return await _channel.Reader.ReadAllAsync(cancellationToken).Take(count).ToArrayAsync(cancellationToken);
        }
    }
}