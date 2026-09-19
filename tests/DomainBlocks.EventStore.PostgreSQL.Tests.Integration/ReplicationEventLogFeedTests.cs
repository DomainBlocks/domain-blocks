using System.Threading.Channels;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.PostgreSQL.Feeds;
using DomainBlocks.Testing.Integration.EventStore;
using DomainBlocks.Testing.Integration.EventStore.PostgreSQL;
using Npgsql;
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
/// Exercises the logical replication session through the feed, against a real server. Events are decoded with a
/// pass-through decoder so that the raw column values can be asserted on.
/// </summary>
[TestFixture]
public class ReplicationEventLogFeedTests : PostgresIntegrationTest
{
    private int _slotCounter;

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
        (events[0].Payload.EventData is string).ShouldBeTrue();
        events[0].Payload.EventData.Json.ShouldBe("{\"a\": 1}");
        events[0].Payload.Metadata.ShouldBe("{\"tenant\": \"x\"}");
        events[0].Context.CreatedAt.Offset.ShouldBe(TimeSpan.Zero);
        events[0].Context.CreatedAt.ShouldBeInRange(before, DateTimeOffset.UtcNow.AddSeconds(1));

        events[1].Context.StreamPosition.ShouldBe(StreamPosition.FromInt64(1));
        events[1].Payload.EventName.ShouldBe("bytes-event");
        (events[1].Payload.EventData is ReadOnlyMemory<byte>).ShouldBeTrue();
        events[1].Payload.EventData.Bytes.ToArray().ShouldBe(bytes);
        events[1].Payload.Metadata.ShouldBeNull();
        events[1].Context.CreatedAt.ShouldBe(events[0].Context.CreatedAt);
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
        var missingNames = new SchemaObjectNames("dbx_no_such_schema");

        var feed = new EventLogFeed<RawReadEvent>(
            token => ReplicationEventLogSession.OpenAsync(
                PostgresTestEnvironment.ConnectionString,
                NextSlotName(),
                missingNames,
                Options.Replication,
                RawDecoder.Instance,
                LoggerFactory.CreateLogger("ReplicationEventLogSession"),
                token),
            new EventLogFeedOptions { RetryDelay = TimeSpan.FromMilliseconds(10), MaxRetryAttempts = 1 },
            LoggerFactory.CreateLogger("EventLogFeed"));

        var observer = new CollectingObserver();
        using var attachment = feed.Attach(observer);

        // The slot is created fine: pgoutput only resolves publications when it decodes the first change.
        await using var connection = await feed.ConnectAsync(ct);
        await Client.AppendAsync([Any("s1", JsonEvent("trigger"))], ct);

        var ex = await observer.Error.WaitAsync(ct);
        ex.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.UndefinedObject);
        await connection.Completion.WaitAsync(ct).ShouldThrowAsync<PostgresException>();
    }

    private EventLogFeed<RawReadEvent> CreateFeed()
    {
        return new EventLogFeed<RawReadEvent>(
            ct => ReplicationEventLogSession.OpenAsync(
                PostgresTestEnvironment.ConnectionString,
                NextSlotName(),
                Names,
                Options.Replication,
                RawDecoder.Instance,
                LoggerFactory.CreateLogger("ReplicationEventLogSession"),
                ct),
            new EventLogFeedOptions { RetryDelay = TimeSpan.FromMilliseconds(100) },
            LoggerFactory.CreateLogger("EventLogFeed"));
    }

    private string NextSlotName() => $"dbx_test_{Interlocked.Increment(ref _slotCounter)}_{Guid.NewGuid():N}"[..40];

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

    private sealed class CollectingObserver : IEventLogObserver<RawReadEvent>
    {
        private readonly Channel<RawReadEvent> _channel = Channel.CreateUnbounded<RawReadEvent>();

        private readonly TaskCompletionSource<Exception> _errorTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<Exception> Error => _errorTcs.Task;

        public ValueTask OnNextAsync(RawReadEvent e, CancellationToken cancellationToken)
        {
            _channel.Writer.TryWrite(e);
            return ValueTask.CompletedTask;
        }

        public ValueTask OnResetAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

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