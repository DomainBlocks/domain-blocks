using System.Threading.Channels;
using DomainBlocks.EventStore.PostgreSQL.Feeds;
using DomainBlocks.Testing.Integration;
using Npgsql;
using NUnit.Framework;
using Shouldly;
using static DomainBlocks.EventStore.PostgreSQL.Tests.Integration.AppendFunctionClient;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration;

/// <summary>
/// Exercises the logical replication session through the feed, against a real server.
/// </summary>
[TestFixture]
public class ReplicationEventLogFeedTests
{
    private const string Schema = "dbx_replication_feed_tests";
    private static readonly PostgresEventStoreOptions Options = new() { Schema = Schema };
    private static readonly SqlNames Names = new(Schema);

    private AppendFunctionClient _client = null!;
    private int _slotCounter;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        await PostgresEventStoreAdmin.EnsureInitializedAsync(SetUpFixture.DataSource, Options);
        _client = new AppendFunctionClient(SetUpFixture.DataSource, Schema);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await PostgresEventStoreAdmin.DropAsync(SetUpFixture.DataSource, Options);
    }

    [SetUp]
    public async Task SetUp()
    {
        await _client.ResetAsync();
    }

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
        await _client.AppendAsync(Any("s1", events[..6]), Any("s2", events[6..]));

        var rows1 = await observer1.ReadAsync(10, ct);
        var rows2 = await observer2.ReadAsync(10, ct);

        rows1.Select(x => x.Position).ShouldBe(Enumerable.Range(0, 10).Select(i => (long)i));
        rows1.Select(x => x.EventName).ShouldBe(events.Select(x => x.Name));
        rows1.Select(x => (x.StreamId, x.StreamPosition)).ShouldBe(
            Enumerable.Range(0, 6).Select(i => ("s1", (long)i))
                .Concat(Enumerable.Range(0, 4).Select(i => ("s2", (long)i))));

        rows2.Select(x => x.Position).ShouldBe(rows1.Select(x => x.Position));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task Connect_OnlyDeliversRowsCommittedAfterConnect(CancellationToken ct)
    {
        await _client.AppendAsync(Any("s1", JsonEvent("before")));

        var feed = CreateFeed();
        var observer = new CollectingObserver();
        using var attachment = feed.Attach(observer);
        await using var connection = await feed.ConnectAsync(ct);

        await _client.AppendAsync(Any("s1", JsonEvent("after")));

        var rows = await observer.ReadAsync(1, ct);
        rows[0].EventName.ShouldBe("after");
        rows[0].Position.ShouldBe(1);
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

        await _client.AppendAsync(Any(
            "stream-1",
            Event.WithJson("json-event", "{\"a\": 1}", "{\"tenant\": \"x\"}"),
            Event.WithBytes("bytes-event", bytes)));

        var rows = await observer.ReadAsync(2, ct);

        rows[0].StreamId.ShouldBe("stream-1");
        rows[0].StreamPosition.ShouldBe(0);
        rows[0].EventName.ShouldBe("json-event");
        rows[0].EventData.IsJson.ShouldBeTrue();
        rows[0].EventData.Json.ShouldBe("{\"a\": 1}");
        rows[0].Metadata.ShouldBe("{\"tenant\": \"x\"}");
        rows[0].CreatedAt.Offset.ShouldBe(TimeSpan.Zero);
        rows[0].CreatedAt.ShouldBeInRange(before, DateTimeOffset.UtcNow.AddSeconds(1));

        rows[1].StreamPosition.ShouldBe(1);
        rows[1].EventName.ShouldBe("bytes-event");
        rows[1].EventData.IsBytes.ShouldBeTrue();
        rows[1].EventData.Bytes.ToArray().ShouldBe(bytes);
        rows[1].Metadata.ShouldBeNull();
        rows[1].CreatedAt.ShouldBe(rows[0].CreatedAt);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task Connect_CreatesTemporarySlotAndDisposeDropsIt(CancellationToken ct)
    {
        var feed = CreateFeed();
        using var attachment = feed.Attach(new CollectingObserver());
        var connection = await feed.ConnectAsync(ct);

        var slots = await GetSlotsAsync(ct);
        var (name, plugin, temporary, active) = slots.ShouldHaveSingleItem();
        temporary.ShouldBeTrue();
        active.ShouldBeTrue();
        plugin.ShouldBe("pgoutput");
        name.ShouldStartWith("dbx_test_");

        await connection.DisposeAsync();

        while ((await GetSlotsAsync(ct)).Count > 0)
            await Task.Delay(50, ct);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task Connect_WhenPublicationIsMissing_Faults(CancellationToken ct)
    {
        var missingNames = new SqlNames("dbx_no_such_schema");

        var feed = new EventLogFeed(
            token => ReplicationEventLogSession.OpenAsync(
                SetUpFixture.ConnectionString,
                NextSlotName(),
                missingNames,
                Options.Replication,
                SetUpFixture.LoggerFactory.CreateLogger("ReplicationEventLogSession"),
                token),
            new EventLogFeedOptions { RetryDelay = TimeSpan.FromMilliseconds(10), MaxRetryAttempts = 1 },
            SetUpFixture.LoggerFactory.CreateLogger("EventLogFeed"));

        var observer = new CollectingObserver();
        using var attachment = feed.Attach(observer);

        // The slot is created fine: pgoutput only resolves publications when it decodes the first change.
        await using var connection = await feed.ConnectAsync(ct);
        await _client.AppendAsync(Any("s1", JsonEvent("trigger")));

        var ex = await observer.Error.WaitAsync(ct);
        ex.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.UndefinedObject);
        await connection.Completion.WaitAsync(ct).ShouldThrowAsync<PostgresException>();
    }

    private EventLogFeed CreateFeed()
    {
        return new EventLogFeed(
            ct => ReplicationEventLogSession.OpenAsync(
                SetUpFixture.ConnectionString,
                NextSlotName(),
                Names,
                Options.Replication,
                SetUpFixture.LoggerFactory.CreateLogger("ReplicationEventLogSession"),
                ct),
            new EventLogFeedOptions { RetryDelay = TimeSpan.FromMilliseconds(100) },
            SetUpFixture.LoggerFactory.CreateLogger("EventLogFeed"));
    }

    private string NextSlotName() => $"dbx_test_{Interlocked.Increment(ref _slotCounter)}_{Guid.NewGuid():N}"[..40];

    private static async Task<List<(string Name, string Plugin, bool Temporary, bool Active)>> GetSlotsAsync(
        CancellationToken ct)
    {
        await using var command = SetUpFixture.DataSource.CreateCommand(
            "SELECT slot_name, plugin, temporary, active FROM pg_replication_slots WHERE slot_name LIKE 'dbx_test_%'");

        var slots = new List<(string, string, bool, bool)>();

        await using var reader = await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
            slots.Add((reader.GetString(0), reader.GetString(1), reader.GetBoolean(2), reader.GetBoolean(3)));

        return slots;
    }

    private sealed class CollectingObserver : IEventLogObserver
    {
        private readonly Channel<EventLogRow> _channel = Channel.CreateUnbounded<EventLogRow>();

        private readonly TaskCompletionSource<Exception> _errorTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<Exception> Error => _errorTcs.Task;

        public ValueTask OnNextAsync(EventLogRow row, CancellationToken cancellationToken)
        {
            _channel.Writer.TryWrite(row);
            return ValueTask.CompletedTask;
        }

        public ValueTask OnResetAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask OnErrorAsync(Exception exception, CancellationToken cancellationToken)
        {
            _errorTcs.TrySetResult(exception);
            return ValueTask.CompletedTask;
        }

        public async Task<EventLogRow[]> ReadAsync(int count, CancellationToken cancellationToken)
        {
            return await _channel.Reader.ReadAllAsync(cancellationToken).Take(count).ToArrayAsync(cancellationToken);
        }
    }
}
