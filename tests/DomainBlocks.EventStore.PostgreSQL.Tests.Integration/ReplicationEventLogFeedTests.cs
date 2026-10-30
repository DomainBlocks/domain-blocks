using System.Threading.Channels;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.Internal.Feeds;
using DomainBlocks.EventStore.PostgreSQL.Feeds;
using DomainBlocks.Testing.Integration.EventStore;
using DomainBlocks.Testing.Integration.EventStore.PostgreSQL;
using Npgsql;
using NpgsqlTypes;
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
    private readonly SlotNameGenerator _slotNameGenerator = new(new SchemaObjectNames("dbx_test"));

    private SchemaObjectNames Names => new(Schema);

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task Connect_WhenRowsAreInserted_NotifiesAttachedObserversInOrder(CancellationToken ct)
    {
        await using var feed = CreateFeed();
        var observer1 = new CollectingObserver();
        var observer2 = new CollectingObserver();
        using var attachment1 = feed.Attach(observer1);
        using var attachment2 = feed.Attach(observer2);
        await feed.ConnectAsync(ct);

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

        await using var feed = CreateFeed();
        var observer = new CollectingObserver();
        using var attachment = feed.Attach(observer);
        await feed.ConnectAsync(ct);

        await Client.AppendAsync([Any("s1", JsonEvent("after"))], ct);

        var events = await observer.ReadAsync(1, ct);
        events[0].Payload.EventName.ShouldBe("after");
        events[0].Context.LogPosition.ShouldBe(LogPosition.FromInt64(1));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task Connect_DecodesAllColumns(CancellationToken ct)
    {
        await using var feed = CreateFeed();
        var observer = new CollectingObserver();
        using var attachment = feed.Attach(observer);
        await feed.ConnectAsync(ct);

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
        await using var feed = CreateFeed();
        var observer = new CollectingObserver();
        using var attachment = feed.Attach(observer);
        await feed.ConnectAsync(ct);

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
    public async Task Connect_WhenWalSenderIsTerminatedMidTransaction_ResumesWithoutRepeatingRows(CancellationToken ct)
    {
        await using var feed = CreateFeed();
        var observer = new CollectingObserver();
        using var attachment = feed.Attach(observer);
        await feed.ConnectAsync(ct);

        // The transaction is far larger than the buffers between the walsender and the feed, so while the observer
        // holds the feed on the first row, the walsender can't send the commit.
        var bytes = new byte[64 * 1024];
        var events = Enumerable.Range(0, 500).Select(i => Event.WithBytes($"e{i}", bytes)).ToArray();
        var hold = observer.HoldNext();
        await Client.AppendAsync([Any("s1", events)], ct);
        await hold.WaitAsync(ct);

        await WaitForWalSenderToBlockAsync(ct);
        await TerminateWalSenderAsync(ct);
        observer.Release();
        await Client.AppendAsync([Any("s1", JsonEvent("marker"))], ct);

        (await observer.ReadThroughAsync("marker", ct))
            .Select(x => x.Context.LogPosition)
            .ShouldBe(Enumerable.Range(0, events.Length + 1).Select(i => LogPosition.FromInt64(i)));

        observer.ResetCount.ShouldBe(0);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task Connect_WhenIdleAmidOtherWrites_AdvancesSlotPastLastRow(CancellationToken ct)
    {
        var options = new PostgresReplicationOptions { WalReceiverStatusInterval = TimeSpan.FromMilliseconds(100) };
        await using var feed = CreateFeed(_slotNameGenerator, options);
        var observer = new CollectingObserver();
        using var attachment = feed.Attach(observer);
        await feed.ConnectAsync(ct);
        await CreateOtherTableAsync(ct);

        await Client.AppendAsync([Any("s1", JsonEvent("e0"))], ct);
        await observer.ReadAsync(1, ct);
        var afterRow = await GetCurrentWalLsnAsync(ct);

        // The server sends the feed nothing for these writes, only keepalives.
        while (!await HasSlotPassedAsync(afterRow, ct))
        {
            await WriteOtherTableAsync(ct);
            await Task.Delay(50, ct);
        }
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task Connect_WhenWalSenderIsTerminatedAmidOtherWrites_DeliversEveryRowOnce(CancellationToken ct)
    {
        var options = new PostgresReplicationOptions { WalReceiverStatusInterval = TimeSpan.FromMilliseconds(10) };
        await using var feed = CreateFeed(_slotNameGenerator, options);
        var observer = new CollectingObserver();
        using var attachment = feed.Attach(observer);
        await feed.ConnectAsync(ct);
        await CreateOtherTableAsync(ct);

        // Each reconnect resumes from the slot, which the feed advances while it waits between transactions.
        using var stop = new CancellationTokenSource();
        var otherWrites = RepeatUntilAsync(WriteOtherTableAsync, TimeSpan.FromMilliseconds(1), stop.Token, ct);

        var terminations = RepeatUntilAsync(
            TerminateWalSenderIfActiveAsync,
            TimeSpan.FromMilliseconds(100),
            stop.Token,
            ct);

        const int count = 2000;

        for (var i = 0; i < count; i++)
            await Client.AppendAsync([Any("s1", JsonEvent($"e{i}"))], ct);

        await stop.CancelAsync();
        await Task.WhenAll(otherWrites, terminations);
        await Client.AppendAsync([Any("s1", JsonEvent("marker"))], ct);

        (await observer.ReadThroughAsync("marker", ct))
            .Select(x => x.Context.LogPosition)
            .ShouldBe(Enumerable.Range(0, count + 1).Select(i => LogPosition.FromInt64(i)));

        observer.ResetCount.ShouldBe(0);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task Connect_Streaming_HoldsPermanentSlotUntilDisposed(CancellationToken ct)
    {
        var feed = CreateFeed();
        var observer = new CollectingObserver();
        using var attachment = feed.Attach(observer);
        await feed.ConnectAsync(ct);

        // The feed connects before it starts streaming, and the slot is active only once it streams.
        await Client.AppendAsync([Any("s1", JsonEvent("e"))], ct);
        await observer.ReadAsync(1, ct);

        var slots = await GetSlotsAsync(ct);
        var (name, plugin, temporary, active) = slots.ShouldHaveSingleItem();
        temporary.ShouldBeFalse();
        active.ShouldBeTrue();
        plugin.ShouldBe("pgoutput");
        name.ShouldStartWith("dbx_test_");

        await feed.DisposeAsync();

        (await GetSlotsAsync(ct)).ShouldBeEmpty();
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task Connect_LeftoverSlots_DropsOnlyExpiredSlotsOfSchema(CancellationToken ct)
    {
        const string schema = "dbx_cleanup";
        var options = new PostgresReplicationOptions { InactiveSlotTimeout = TimeSpan.FromSeconds(1) };
        var slotNameGenerator = new SlotNameGenerator(new SchemaObjectNames(schema));

        // Slots that other stores on the schema left behind, as a process that stops without dropping its slot does, a
        // slot of a schema whose name starts with this one, and a slot that no store named.
        var orphan = slotNameGenerator.Next();
        var longerSchema = new SlotNameGenerator(new SchemaObjectNames($"{schema}_x")).Next();
        var recent = slotNameGenerator.Next();
        const string notStoreNamed = "dbx_cleanup_orphan";

        try
        {
            await using var running = CreateFeed(slotNameGenerator, options);
            using var runningAttachment = running.Attach(new CollectingObserver());
            await running.ConnectAsync(ct);

            await CreateSlotAsync(orphan, ct);
            await CreateSlotAsync(longerSchema, ct);
            await CreateSlotAsync(notStoreNamed, ct);
            await Task.Delay(options.InactiveSlotTimeout * 2, ct);
            await CreateSlotAsync(recent, ct);

            await using var starting = CreateFeed(slotNameGenerator, options);
            using var startingAttachment = starting.Attach(new CollectingObserver());
            await starting.ConnectAsync(ct);

            // Only the orphan is dropped. The running and starting feeds' slots are kept as well.
            var names = await GetSlotNamesAsync(schema, ct);
            names.ShouldNotContain(orphan);
            names.ShouldContain(longerSchema);
            names.ShouldContain(notStoreNamed);
            names.ShouldContain(recent);
            names.Count.ShouldBe(5);
        }
        finally
        {
            foreach (var name in new[] { orphan, longerSchema, notStoreNamed, recent })
                await DropSlotIfExistsAsync(name);
        }
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task Connect_LeftoverSlotAfterStart_DropsSlotWhileRunning(CancellationToken ct)
    {
        const string schema = "dbx_sweep";
        var options = new PostgresReplicationOptions { InactiveSlotTimeout = TimeSpan.FromSeconds(1) };
        var slotNameGenerator = new SlotNameGenerator(new SchemaObjectNames(schema));
        var leftover = slotNameGenerator.Next();

        try
        {
            await using var feed = CreateFeed(slotNameGenerator, options);
            using var attachment = feed.Attach(new CollectingObserver());
            await feed.ConnectAsync(ct);

            // The feed has created its slot, so only the timer can drop a slot that another store leaves behind now.
            await CreateSlotAsync(leftover, ct);

            while ((await GetSlotNamesAsync(schema, ct)).Contains(leftover))
                await Task.Delay(100, ct);

            (await GetSlotNamesAsync(schema, ct)).Count.ShouldBe(1);
        }
        finally
        {
            await DropSlotIfExistsAsync(leftover);
        }
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task Connect_WhenPublicationIsMissing_Faults(CancellationToken ct)
    {
        var options = new PostgresEventStoreOptions { Schema = "dbx_no_publication" };
        var adminOptions = new PostgresEventStoreAdminOptions { CreatePublication = false };
        await PostgresEventStoreAdmin.EnsureInitializedAsync(DataSource, options, adminOptions, ct);

        await using var feed = CreateFeed(
            _slotNameGenerator,
            new SchemaObjectNames(options.Schema),
            Options.Replication,
            new FeedResumeOptions { RetryDelay = TimeSpan.FromMilliseconds(10), MaxRetryAttempts = 1 });

        var observer = new CollectingObserver();
        using var attachment = feed.Attach(observer);

        // The slot is created without error, because pgoutput only resolves publications when it decodes the first
        // change.
        await feed.ConnectAsync(ct);
        await Client.AppendAsync([Any("s1", JsonEvent("trigger"))], ct);

        var ex = await observer.Error.WaitAsync(ct);
        ex.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.UndefinedObject);
        await feed.Completion.WaitAsync(ct).ShouldThrowAsync<PostgresException>();
    }

    private ReplicationEventLogFeed<RawEvent> CreateFeed() =>
        CreateFeed(_slotNameGenerator, Options.Replication);

    private ReplicationEventLogFeed<RawEvent> CreateFeed(
        SlotNameGenerator slotNameGenerator,
        PostgresReplicationOptions replicationOptions)
    {
        return CreateFeed(
            slotNameGenerator,
            Names,
            replicationOptions,
            new FeedResumeOptions { RetryDelay = TimeSpan.FromMilliseconds(100) });
    }

    private ReplicationEventLogFeed<RawEvent> CreateFeed(
        SlotNameGenerator slotNameGenerator,
        SchemaObjectNames names,
        PostgresReplicationOptions replicationOptions,
        FeedResumeOptions resumeOptions)
    {
        return new ReplicationEventLogFeed<RawEvent>(
            PostgresTestEnvironment.ConnectionString,
            DataSource,
            slotNameGenerator,
            names,
            replicationOptions,
            RawDecoder.Instance,
            new FeedResumePolicy(EventLogFeedTransientErrors.IsTransient, resumeOptions),
            LoggerFactory.CreateLogger("EventLogFeed"));
    }

    private static async Task RepeatUntilAsync(
        Func<CancellationToken, Task> action,
        TimeSpan interval,
        CancellationToken stop,
        CancellationToken ct)
    {
        while (!stop.IsCancellationRequested)
        {
            await action(ct);
            await Task.Delay(interval, ct);
        }
    }

    private static async Task TerminateWalSenderAsync(CancellationToken ct)
    {
        await using var command = PostgresTestEnvironment.DataSource.CreateCommand(
            "SELECT pg_terminate_backend(active_pid) FROM pg_replication_slots " +
            "WHERE slot_name LIKE 'dbx_test_%' AND active_pid IS NOT NULL");

        (await command.ExecuteScalarAsync(ct)).ShouldBe(true);
    }

    // The feed may be reconnecting, so there may be no walsender to end.
    private static async Task TerminateWalSenderIfActiveAsync(CancellationToken ct)
    {
        await using var command = PostgresTestEnvironment.DataSource.CreateCommand(
            "SELECT pg_terminate_backend(active_pid) FROM pg_replication_slots " +
            "WHERE slot_name LIKE 'dbx_test_%' AND active_pid IS NOT NULL");

        await command.ExecuteNonQueryAsync(ct);
    }

    // A table outside the publication, as another application sharing the database would have.
    private async Task CreateOtherTableAsync(CancellationToken ct)
    {
        await using var command = DataSource.CreateCommand($"CREATE TABLE IF NOT EXISTS {Schema}.other (value bigint)");
        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task WriteOtherTableAsync(CancellationToken ct)
    {
        await using var command = DataSource.CreateCommand($"INSERT INTO {Schema}.other VALUES (1)");
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<NpgsqlLogSequenceNumber> GetCurrentWalLsnAsync(CancellationToken ct)
    {
        await using var command = PostgresTestEnvironment.DataSource.CreateCommand("SELECT pg_current_wal_lsn()");
        return (NpgsqlLogSequenceNumber)(await command.ExecuteScalarAsync(ct)).ShouldNotBeNull();
    }

    private static async Task<bool> HasSlotPassedAsync(NpgsqlLogSequenceNumber lsn, CancellationToken ct)
    {
        await using var command = PostgresTestEnvironment.DataSource.CreateCommand(
            "SELECT confirmed_flush_lsn > $1 FROM pg_replication_slots WHERE slot_name LIKE 'dbx_test_%'");

        command.Parameters.Add(new NpgsqlParameter<NpgsqlLogSequenceNumber>
        {
            TypedValue = lsn,
            NpgsqlDbType = NpgsqlDbType.PgLsn
        });

        return (bool)(await command.ExecuteScalarAsync(ct)).ShouldNotBeNull();
    }

    private static async Task WaitForWalSenderToBlockAsync(CancellationToken ct)
    {
        await using var command = PostgresTestEnvironment.DataSource.CreateCommand(
            "SELECT EXISTS (SELECT FROM pg_stat_activity a JOIN pg_replication_slots s ON a.pid = s.active_pid " +
            "WHERE s.slot_name LIKE 'dbx_test_%' AND a.wait_event = 'WalSenderWriteData')");

        while (await command.ExecuteScalarAsync(ct) is not true)
            await Task.Delay(10, ct);
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

    private sealed class CollectingObserver : IFeedObserver<EventLogRow<RawEvent>>
    {
        private readonly Channel<RawReadEvent> _channel = Channel.CreateUnbounded<RawReadEvent>();

        private readonly TaskCompletionSource<Exception> _errorTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource _releaseTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource? _holdTcs;

        public Task<Exception> Error => _errorTcs.Task;

        public int ResetCount { get; private set; }

        /// <summary>
        /// Holds the feed in the next row's notification, after the row is collected, until <see cref="Release"/>.
        /// The returned task completes once the feed is held.
        /// </summary>
        public Task HoldNext()
        {
            var holdTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref _holdTcs, holdTcs);
            return holdTcs.Task;
        }

        public void Release() => _releaseTcs.TrySetResult();

        public async ValueTask OnNextAsync(EventLogRow<RawEvent> row, CancellationToken cancellationToken)
        {
            // The row is only valid during this call.
            _channel.Writer.TryWrite(ReadEvent.Create(row.Payload, row.Metadata, row.Context));

            if (Interlocked.Exchange(ref _holdTcs, null) is { } holdTcs)
            {
                holdTcs.SetResult();
                await _releaseTcs.Task.WaitAsync(cancellationToken);
            }
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

        public async Task<List<RawReadEvent>> ReadThroughAsync(string eventName, CancellationToken cancellationToken)
        {
            var events = new List<RawReadEvent>();

            while (events.Count == 0 || events[^1].Payload.EventName != eventName)
                events.Add(await _channel.Reader.ReadAsync(cancellationToken));

            return events;
        }
    }
}