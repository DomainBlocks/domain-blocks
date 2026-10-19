using DomainBlocks.Testing.Events;
using DomainBlocks.Testing.Integration.EventStore;
using DomainBlocks.Testing.Integration.EventStore.MongoDB;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Configuration;
using MongoDB.Driver.Core.Events;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

/// <summary>
/// MongoDB's subscription source: the operation time high-water mark that makes a catch-up see what the tail has
/// delivered, and the change stream that wakes the tail.
/// </summary>
[TestFixture]
public class MongoSubscriptionTailTests
{
    // See: https://github.com/mongodb/mongo/blob/r7.0.16/src/mongo/base/error_codes.yml
    private const int BadValue = 2;
    private const int NetworkTimeout = 89;

    private readonly MongoEventStoreOptions _options = new() { DatabaseName = $"tail_{Guid.NewGuid():N}" };

    [TearDown]
    public Task TearDown() => MongoTestEnvironment.MongoClient.DropDatabaseAsync(_options.DatabaseName);

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_CatchUpWhileTailRuns_ReadsAfterTheTailsLatestOperationTime(CancellationToken ct)
    {
        var commands = new CommandRecorder();
        using var client = CreateClient(commands);
        await using var eventStore = CreateEventStore(client);
        await eventStore.EnsureInitializedAsync(ct);

        await using var running = eventStore.SubscribeToAll().GetAsyncEnumerator(ct);
        await ShouldBeCaughtUpAsync(running);
        var appended = await AppendAsync(eventStore, 3, ct);
        (await NextEventsAsync(running, appended.Length)).ShouldBe(appended);

        var tailTime = commands.LatestTailReadTime();
        var mark = commands.Count;

        await using var catchingUp = eventStore.SubscribeToAll(SubscriptionOrigin.Start).GetAsyncEnumerator(ct);
        (await NextEventsAsync(catchingUp, appended.Length)).ShouldBe(appended);
        await ShouldBeCaughtUpAsync(catchingUp);

        // The catch-up's first read is its end read, which the server holds until it has seen the tail's reads.
        var endRead = commands.FirstEndReadAfter(mark);
        var afterClusterTime = endRead["readConcern"]["afterClusterTime"].AsBsonTimestamp;
        afterClusterTime.CompareTo(tailTime).ShouldBeGreaterThanOrEqualTo(0);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_EventsAppended_DeliversThemWithoutPolling(CancellationToken ct)
    {
        var options = WithTail(new EventLogTailOptions { PollInterval = TimeSpan.FromHours(1) });
        await using var eventStore = CreateEventStore(MongoTestEnvironment.MongoClient, options);
        await eventStore.EnsureInitializedAsync(ct);

        await using var subscription = eventStore.SubscribeToAll().GetAsyncEnumerator(ct);
        await ShouldBeCaughtUpAsync(subscription);

        // The poll interval is longer than the test, so each event arrives only because its insert was heard.
        for (var i = 0; i < 3; i++)
        {
            var appended = await AppendAsync(eventStore, 1, ct);
            (await NextEventsAsync(subscription, 1)).ShouldBe(appended);
        }
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_ListeningFails_DeliversEventsWithinThePollInterval(CancellationToken ct)
    {
        var applicationName = $"dbx-listen-{Guid.NewGuid():N}"[..20];
        var commands = new CommandRecorder();
        using var client = CreateClient(commands, applicationName);

        var options = WithTail(new EventLogTailOptions
        {
            PollInterval = TimeSpan.FromMilliseconds(200),
            RetryDelay = TimeSpan.FromMilliseconds(100),
            MaxRetryDelay = TimeSpan.FromMilliseconds(500)
        });

        await using var eventStore = CreateEventStore(client, options);
        await eventStore.EnsureInitializedAsync(ct);

        // Appends send aggregates too, so they go through a client that the fail point does not fail.
        await using var appender = CreateEventStore(MongoTestEnvironment.MongoClient, options);

        await using var subscription = eventStore.SubscribeToAll().GetAsyncEnumerator(ct);
        await ShouldBeCaughtUpAsync(subscription);

        // The change stream fails, and so does every attempt to open a new one, while the fail point is on. The error
        // is one that the driver does not resume from.
        await SetFailPointAsync(applicationName, "alwaysOn", ["aggregate", "getMore"], BadValue, ct);

        try
        {
            await commands.WaitForFailedChangeStreamCommandAsync(ct);

            var appended = await AppendAsync(appender, 5, ct);
            (await NextEventsAsync(subscription, appended.Length)).ShouldBe(appended);
        }
        finally
        {
            await SetFailPointAsync(applicationName, "off", [], BadValue, CancellationToken.None);
        }
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_TailReadFailsTransiently_RetriesAndDeliversEvents(CancellationToken ct)
    {
        var applicationName = $"dbx-read-{Guid.NewGuid():N}"[..20];
        using var client = CreateClient(new CommandRecorder(), applicationName);

        var options = WithTail(new EventLogTailOptions
        {
            RetryDelay = TimeSpan.FromMilliseconds(100),
            MaxRetryDelay = TimeSpan.FromMilliseconds(500)
        });

        await using var eventStore = CreateEventStore(client, options);
        await eventStore.EnsureInitializedAsync(ct);
        await using var appender = CreateEventStore(MongoTestEnvironment.MongoClient, options);

        await using var subscription = eventStore.SubscribeToAll().GetAsyncEnumerator(ct);
        await ShouldBeCaughtUpAsync(subscription);

        // The tail's next read fails, and so does the driver's own retry of it, so the tail must retry the read.
        await SetFailPointAsync(applicationName, new BsonDocument("times", 2), ["find"], NetworkTimeout, ct);

        try
        {
            var appended = await AppendAsync(appender, 3, ct);
            (await NextEventsAsync(subscription, appended.Length)).ShouldBe(appended);
        }
        finally
        {
            await SetFailPointAsync(applicationName, "off", [], NetworkTimeout, CancellationToken.None);
        }
    }

    private MongoEventStoreOptions WithTail(EventLogTailOptions tail) => new()
    {
        DatabaseName = _options.DatabaseName,
        Tail = tail
    };

    private IEventStore<object, string, StreamPosition, LogPosition> CreateEventStore(
        IMongoClient client,
        MongoEventStoreOptions? options = null)
    {
        return new MongoEventStoreBuilder<object>()
            .UseClient(client)
            .UseOptions(options ?? _options)
            .ConfigureCodec(x => x.MapEvent<TestEvent>())
            .Build();
    }

    private static MongoClient CreateClient(CommandRecorder commands, string? applicationName = null)
    {
        var settings = MongoClientSettings.FromConnectionString(MongoTestEnvironment.ConnectionString);
        settings.ApplicationName = applicationName;
        settings.ClusterConfigurator = commands.SubscribeTo;

        return new MongoClient(settings);
    }

    // Fails the given commands with the given error, for this test's client only.
    private static async Task SetFailPointAsync(
        string applicationName,
        BsonValue mode,
        string[] commandNames,
        int errorCode,
        CancellationToken ct)
    {
        var command = new BsonDocument
        {
            { "configureFailPoint", "failCommand" },
            { "mode", mode },
            {
                "data", new BsonDocument
                {
                    { "failCommands", new BsonArray(commandNames) },
                    { "errorCode", errorCode },
                    { "appName", applicationName }
                }
            }
        };

        await MongoTestEnvironment.MongoClient
            .GetDatabase("admin")
            .RunCommandAsync<BsonDocument>(command, cancellationToken: ct);
    }

    private static async Task<TestEvent[]> AppendAsync(
        IEventStore<object, string, StreamPosition, LogPosition> eventStore,
        int count,
        CancellationToken ct)
    {
        var events = Enumerable
            .Range(0, count)
            .Select(_ => new TestEvent { Value = Guid.NewGuid().ToString() })
            .ToArray();

        foreach (var e in events)
            await eventStore.AppendAsync("s1", [AppendableEvent.Create<object>(e)], cancellationToken: ct);

        return events;
    }

    // Reads the next messages, which must all be events.
    private static async Task<TestEvent[]> NextEventsAsync(
        IAsyncEnumerator<SubscriptionMessage<object, string, StreamPosition, LogPosition>> enumerator,
        int count)
    {
        var events = new TestEvent[count];

        for (var i = 0; i < count; i++)
        {
            (await enumerator.MoveNextAsync()).ShouldBeTrue();
            events[i] = enumerator.Current.Event.ShouldNotBeNull().Payload.ShouldBeOfType<TestEvent>();
        }

        return events;
    }

    private static async Task ShouldBeCaughtUpAsync(
        IAsyncEnumerator<SubscriptionMessage<object, string, StreamPosition, LogPosition>> enumerator)
    {
        (await enumerator.MoveNextAsync()).ShouldBeTrue();
        enumerator.Current.Kind.ShouldBe(SubscriptionMessageKind.CaughtUp);
    }

    /// <summary>
    /// Records the finds that the store sends, with the operation time of each reply, and the change stream commands
    /// that fail.
    /// </summary>
    private sealed class CommandRecorder
    {
        private readonly Lock _lock = new();
        private readonly List<(long RequestId, BsonDocument Command)> _finds = [];
        private readonly Dictionary<long, BsonTimestamp> _replyTimes = [];
        private readonly TaskCompletionSource _changeStreamFailed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Count
        {
            get
            {
                lock (_lock)
                    return _finds.Count;
            }
        }

        public void SubscribeTo(ClusterBuilder builder)
        {
            builder.Subscribe<CommandStartedEvent>(e =>
            {
                if (e.CommandName == "find")
                {
                    lock (_lock)
                        _finds.Add((e.RequestId, e.Command.DeepClone().AsBsonDocument));
                }
            });

            builder.Subscribe<CommandSucceededEvent>(e =>
            {
                if (e.CommandName == "find" && e.Reply.TryGetValue("operationTime", out var time))
                {
                    lock (_lock)
                        _replyTimes[e.RequestId] = time.AsBsonTimestamp;
                }
            });

            builder.Subscribe<CommandFailedEvent>(e =>
            {
                if (e.CommandName is "aggregate" or "getMore")
                    _changeStreamFailed.TrySetResult();
            });
        }

        public Task WaitForFailedChangeStreamCommandAsync(CancellationToken ct)
        {
            return _changeStreamFailed.Task.WaitAsync(ct);
        }

        /// <summary>
        /// Gets the latest operation time of a tail read: a find after a position, with no end.
        /// </summary>
        public BsonTimestamp LatestTailReadTime()
        {
            lock (_lock)
            {
                return _finds
                    .Where(x => IsTailRead(x.Command))
                    .Max(x => _replyTimes.GetValueOrDefault(x.RequestId))
                    .ShouldNotBeNull();
            }
        }

        /// <summary>
        /// Gets the first read of an end position, a find sorted by descending position, after the given count of
        /// finds.
        /// </summary>
        public BsonDocument FirstEndReadAfter(int count)
        {
            lock (_lock)
                return _finds.Skip(count).First(x => IsEndRead(x.Command)).Command;
        }

        private static bool IsTailRead(BsonDocument command) =>
            command["filter"].AsBsonDocument.TryGetValue(EventLogEntry.FieldNames.Position, out var range) &&
            range.AsBsonDocument.Contains("$gt") &&
            !range.AsBsonDocument.Contains("$lte");

        private static bool IsEndRead(BsonDocument command) =>
            command.TryGetValue("sort", out var sort) &&
            sort.AsBsonDocument.TryGetValue(EventLogEntry.FieldNames.Position, out var direction) &&
            direction.ToInt32() == -1;
    }
}