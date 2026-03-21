using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Client.Coordination;
using DomainBlocks.EventStore.MongoDB.Client.Schema2;
using DomainBlocks.Testing.Integration.MongoDB;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.MongoDB.Client.Tests.Integration.Coordination;

public class EventAppenderTests
{
    private const long Epoch = 1;

    private MongoClient _mongoClient = null!;
    private ILoggerFactory _loggerFactory = null!;
    private IMongoCollection<EventLogEntry> _eventLog = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _mongoClient = new MongoClient(MongoConnectionStrings.Default);
        _loggerFactory = LoggerFactory.Create(x => x.AddConsole().SetMinimumLevel(LogLevel.Debug));

        var ns = EventStoreNamespaceSettings.Default with { DatabaseName = "domainblocks_tests" };

        await MongoEventStoreAdmin2.EnsureInitializedAsync(_mongoClient, ns);

        var db = _mongoClient.GetDatabase(ns.DatabaseName);
        _eventLog = db.GetCollection<EventLogEntry>(ns.EventLogCollectionName);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _eventLog.DeleteManyAsync(Builders<EventLogEntry>.Filter.Empty);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _mongoClient.Dispose();
        _loggerFactory.Dispose();
    }

    // Basic append

    [Test]
    public async Task AppendBatchAsync_SingleRequest_AppendsEventsWithCorrectPositionsAndVersions()
    {
        var appender = CreateAppender();
        var commitId = Guid.CreateVersion7();
        var streamId = CreateStreamId();

        var requests = new[]
        {
            CreateRequest(commitId, streamId, ExpectedStreamState.Any, "EventA", "EventB", "EventC")
        };

        await appender.AppendBatchAsync(requests);

        var entries = await ReadEventEntries();
        entries.Count.ShouldBe(3);

        entries[0].Position.ShouldBe(0);
        entries[0].Epoch.ShouldBe(Epoch);
        entries[0].StreamId.ShouldBe(streamId);
        entries[0].StreamVersion.ShouldBe(0);
        entries[0].CommitId.ShouldBe(commitId);
        entries[0].EventName.ShouldBe("EventA");

        entries[1].Position.ShouldBe(1);
        entries[1].StreamVersion.ShouldBe(1);
        entries[1].EventName.ShouldBe("EventB");

        entries[2].Position.ShouldBe(2);
        entries[2].StreamVersion.ShouldBe(2);
        entries[2].EventName.ShouldBe("EventC");

        var batchCompleted = await ReadLastBatchCompleted();
        batchCompleted.Appends.ShouldBe([commitId]);
        batchCompleted.Duplicates.ShouldBeEmpty();
        batchCompleted.Rejections.ShouldBeEmpty();
    }

    [Test]
    public async Task AppendBatchAsync_MultipleRequestsInSingleBatch_AppendsAllWithContiguousPositions()
    {
        var appender = CreateAppender();
        var streamA = CreateStreamId();
        var streamB = CreateStreamId();
        var commitA = Guid.CreateVersion7();
        var commitB = Guid.CreateVersion7();

        var requests = new[]
        {
            CreateRequest(commitA, streamA, ExpectedStreamState.Any, "A1", "A2"),
            CreateRequest(commitB, streamB, ExpectedStreamState.Any, "B1")
        };

        await appender.AppendBatchAsync(requests);

        var entries = await ReadEventEntries();
        entries.Count.ShouldBe(3);

        // Stream A events
        entries[0].Position.ShouldBe(0);
        entries[0].StreamId.ShouldBe(streamA);
        entries[0].StreamVersion.ShouldBe(0);

        entries[1].Position.ShouldBe(1);
        entries[1].StreamId.ShouldBe(streamA);
        entries[1].StreamVersion.ShouldBe(1);

        // Stream B events
        entries[2].Position.ShouldBe(2);
        entries[2].StreamId.ShouldBe(streamB);
        entries[2].StreamVersion.ShouldBe(0);

        var batchCompleted = await ReadLastBatchCompleted();
        batchCompleted.Appends.ShouldContain(commitA);
        batchCompleted.Appends.ShouldContain(commitB);
        batchCompleted.Duplicates.ShouldBeEmpty();
        batchCompleted.Rejections.ShouldBeEmpty();
    }

    [Test]
    public async Task AppendBatchAsync_MultipleBatchesOnSameStream_ContinuesPositionAndVersion()
    {
        var appender = CreateAppender();
        var streamId = CreateStreamId();

        await appender.AppendBatchAsync([
            CreateRequest(Guid.CreateVersion7(), streamId, ExpectedStreamState.Any, "E1", "E2")
        ]);

        await appender.AppendBatchAsync([
            CreateRequest(Guid.CreateVersion7(), streamId, ExpectedStreamState.Any, "E3")
        ]);

        var entries = await ReadEventEntries();
        entries.Count.ShouldBe(3);

        entries[0].Position.ShouldBe(0);
        entries[0].StreamVersion.ShouldBe(0);

        entries[1].Position.ShouldBe(1);
        entries[1].StreamVersion.ShouldBe(1);

        entries[2].Position.ShouldBe(3); // An extra position should be taken by marker event
        entries[2].StreamVersion.ShouldBe(2);
    }

    // Empty / no-op

    [Test]
    public async Task AppendBatchAsync_EmptyInput_WritesNothing()
    {
        var appender = CreateAppender();

        await appender.AppendBatchAsync([]);

        var all = await ReadAllEntries();
        all.ShouldBeEmpty();
    }

    [Test]
    public async Task AppendBatchAsync_RequestWithNoEvents_WritesNothing()
    {
        var appender = CreateAppender();

        var requests = new[]
        {
            CreateRequest(Guid.CreateVersion7(), CreateStreamId(), ExpectedStreamState.Any)
        };

        await appender.AppendBatchAsync(requests);

        var all = await ReadAllEntries();
        all.ShouldBeEmpty();
    }

    // Duplicate detection (idempotency)

    [Test]
    public async Task AppendBatchAsync_SameCommitIdInTwoSeparateBatches_SecondBatchTreatsDuplicate()
    {
        var appender = CreateAppender();
        var commitId = Guid.CreateVersion7();
        var streamId = CreateStreamId();

        await appender.AppendBatchAsync([
            CreateRequest(commitId, streamId, ExpectedStreamState.Any, "E1")
        ]);

        await appender.AppendBatchAsync([
            CreateRequest(commitId, streamId, ExpectedStreamState.Any, "E1")
        ]);

        // Only one event should exist — the duplicate wasn't re-appended.
        var entries = await ReadEventEntries();
        entries.Count.ShouldBe(1);

        // Second batch should still produce a batch-completed marker with the duplicate.
        var allEntries = await ReadAllEntries();
        var batchCompletedEntries = allEntries.Where(e => e.EventName == "AppendBatchCompleted").ToArray();
        batchCompletedEntries.Length.ShouldBe(2);

        var secondMarker = batchCompletedEntries.OrderBy(e => e.Position).Last();
        var secondCompleted = BsonSerializer.Deserialize<AppendBatchCompleted>(secondMarker.EventData.AsBsonDocument);
        secondCompleted.Duplicates.ShouldContain(commitId);
        secondCompleted.Appends.ShouldBeEmpty();
    }

    [Test]
    public async Task AppendBatchAsync_SameCommitIdRepeatedWithinSameBatch_OnlyFirstIsAppended()
    {
        var appender = CreateAppender();
        var commitId = Guid.CreateVersion7();
        var streamId = CreateStreamId();

        var requests = new[]
        {
            CreateRequest(commitId, streamId, ExpectedStreamState.Any, "E1"),
            CreateRequest(commitId, streamId, ExpectedStreamState.Any, "E1")
        };

        await appender.AppendBatchAsync(requests);

        var entries = await ReadEventEntries();
        entries.Count.ShouldBe(1);
    }

    // OCC: ExpectedStreamState.StreamDoesNotExist

    [Test]
    public async Task AppendBatchAsync_StreamDoesNotExist_AndStreamIsNew_Appends()
    {
        var appender = CreateAppender();
        var commitId = Guid.CreateVersion7();
        var streamId = CreateStreamId();

        await appender.AppendBatchAsync([
            CreateRequest(commitId, streamId, ExpectedStreamState.StreamDoesNotExist, "E1")
        ]);

        var entries = await ReadEventEntries();
        entries.Count.ShouldBe(1);
    }

    [Test]
    public async Task AppendBatchAsync_StreamDoesNotExist_ButStreamAlreadyExists_Rejects()
    {
        var appender = CreateAppender();
        var streamId = CreateStreamId();

        // First: create the stream.
        await appender.AppendBatchAsync([
            CreateRequest(Guid.CreateVersion7(), streamId, ExpectedStreamState.Any, "E1")
        ]);

        // Second: attempt with StreamDoesNotExist — should be rejected.
        var rejectedCommitId = Guid.CreateVersion7();

        await appender.AppendBatchAsync([
            CreateRequest(rejectedCommitId, streamId, ExpectedStreamState.StreamDoesNotExist, "E2")
        ]);

        // Only the first event should exist.
        var entries = await ReadEventEntries();
        entries.Count.ShouldBe(1);

        var batchCompleted = await ReadLastBatchCompleted();
        batchCompleted.Rejections.Count.ShouldBe(1);

        var rejection = batchCompleted.Rejections.First();
        rejection.CommitId.ShouldBe(rejectedCommitId);
        rejection.StreamId.ShouldBe(streamId);
        rejection.ExpectedStreamState.ShouldBe(ExpectedStreamState.StreamDoesNotExist);
        rejection.ActualStreamState.ShouldBe(StreamState.StreamExists(StreamVersion.FromInt64(0)));
    }

    // OCC: ExpectedStreamState.StreamExists

    [Test]
    public async Task AppendBatchAsync_StreamExists_AndStreamHasEvents_Appends()
    {
        var appender = CreateAppender();
        var streamId = CreateStreamId();

        await appender.AppendBatchAsync([
            CreateRequest(Guid.CreateVersion7(), streamId, ExpectedStreamState.Any, "E1")
        ]);

        var commitId = Guid.CreateVersion7();

        await appender.AppendBatchAsync([
            CreateRequest(commitId, streamId, ExpectedStreamState.StreamExists, "E2")
        ]);

        var entries = await ReadEventEntries();
        entries.Count.ShouldBe(2);
        entries[1].EventName.ShouldBe("E2");
    }

    [Test]
    public async Task AppendBatchAsync_StreamExists_ButStreamIsNew_Rejects()
    {
        var appender = CreateAppender();
        var rejectedCommitId = Guid.CreateVersion7();
        var streamId = CreateStreamId();

        await appender.AppendBatchAsync([
            CreateRequest(rejectedCommitId, streamId, ExpectedStreamState.StreamExists, "E1")
        ]);

        var entries = await ReadEventEntries();
        entries.ShouldBeEmpty();

        var batchCompleted = await ReadLastBatchCompleted();
        batchCompleted.Rejections.Count.ShouldBe(1);

        var rejection = batchCompleted.Rejections.First();
        rejection.CommitId.ShouldBe(rejectedCommitId);
        rejection.StreamId.ShouldBe(streamId);
        rejection.ExpectedStreamState.ShouldBe(ExpectedStreamState.StreamExists);
        rejection.ActualStreamState.ShouldBe(StreamState.StreamDoesNotExist);
    }

    // OCC: ExpectedStreamState.SpecificVersion

    [Test]
    public async Task AppendBatchAsync_SpecificVersion_MatchesActual_Appends()
    {
        var appender = CreateAppender();
        var streamId = CreateStreamId();

        // Append two events → stream at version 1.
        await appender.AppendBatchAsync([
            CreateRequest(Guid.CreateVersion7(), streamId, ExpectedStreamState.Any, "E1", "E2")
        ]);

        // Expect version 1 — should succeed.
        var commitId = Guid.CreateVersion7();

        await appender.AppendBatchAsync([
            CreateRequest(commitId, streamId, ExpectedStreamState.SpecificVersion(new StreamVersion(1)), "E3")
        ]);

        var entries = await ReadEventEntries();
        entries.Count.ShouldBe(3);
        entries[2].EventName.ShouldBe("E3");
        entries[2].StreamVersion.ShouldBe(2);
    }

    [Test]
    public async Task AppendBatchAsync_SpecificVersion_DoesNotMatchActual_Rejects()
    {
        var appender = CreateAppender();
        var streamId = CreateStreamId();

        // Append two events → stream at version 1.
        await appender.AppendBatchAsync([
            CreateRequest(Guid.CreateVersion7(), streamId, ExpectedStreamState.Any, "E1", "E2")
        ]);

        // Expect version 0 (stale) — should be rejected.
        var rejectedCommitId = Guid.CreateVersion7();

        await appender.AppendBatchAsync([
            CreateRequest(
                rejectedCommitId,
                streamId,
                ExpectedStreamState.SpecificVersion(new StreamVersion(0)),
                "E3")
        ]);

        var entries = await ReadEventEntries();
        entries.Count.ShouldBe(2); // Only the original two.

        var batchCompleted = await ReadLastBatchCompleted();
        var rejection = batchCompleted.Rejections.First();
        rejection.CommitId.ShouldBe(rejectedCommitId);
        rejection.StreamId.ShouldBe(streamId);
        rejection.ExpectedStreamState.ShouldBe(ExpectedStreamState.SpecificVersion(new StreamVersion(0)));
        rejection.ActualStreamState.ShouldBe(StreamState.StreamExists(new StreamVersion(1)));
    }

    [Test]
    public async Task AppendBatchAsync_SpecificVersion_StreamDoesNotExist_Rejects()
    {
        var appender = CreateAppender();
        var streamId = CreateStreamId();
        var rejectedCommitId = Guid.CreateVersion7();

        await appender.AppendBatchAsync([
            CreateRequest(
                rejectedCommitId,
                streamId,
                ExpectedStreamState.SpecificVersion(new StreamVersion(0)),
                "E1")
        ]);

        var entries = await ReadEventEntries();
        entries.ShouldBeEmpty();

        var batchCompleted = await ReadLastBatchCompleted();
        var rejection = batchCompleted.Rejections.First();
        rejection.CommitId.ShouldBe(rejectedCommitId);
        rejection.StreamId.ShouldBe(streamId);
        rejection.ExpectedStreamState.ShouldBe(ExpectedStreamState.SpecificVersion(new StreamVersion(0)));
        rejection.ActualStreamState.ShouldBe(StreamState.StreamDoesNotExist);
    }

    // OCC within a single batch

    [Test]
    public async Task AppendBatchAsync_TwoRequestsForSameStream_SecondSeesFirstsVersion()
    {
        var appender = CreateAppender();
        var streamId = CreateStreamId();

        var requests = new[]
        {
            CreateRequest(Guid.CreateVersion7(), streamId, ExpectedStreamState.StreamDoesNotExist, "E1"),
            CreateRequest(Guid.CreateVersion7(), streamId, ExpectedStreamState.StreamExists, "E2")
        };

        await appender.AppendBatchAsync(requests);

        var entries = await ReadEventEntries();
        entries.Count.ShouldBe(2);
        entries[0].StreamVersion.ShouldBe(0);
        entries[1].StreamVersion.ShouldBe(1);
    }

    [Test]
    public async Task AppendBatchAsync_RejectionDoesNotAdvanceStreamVersion()
    {
        var appender = CreateAppender();
        var streamId = CreateStreamId();

        var requests = new[]
        {
            CreateRequest(Guid.CreateVersion7(), streamId, ExpectedStreamState.Any, "E1"),
            CreateRequest(Guid.CreateVersion7(), streamId, ExpectedStreamState.StreamDoesNotExist, "Rejected"),

            CreateRequest(
                Guid.CreateVersion7(),
                streamId,
                ExpectedStreamState.SpecificVersion(new StreamVersion(0)),
                "E2")
        };

        await appender.AppendBatchAsync(requests);

        var entries = await ReadEventEntries();
        entries.Count.ShouldBe(2);
        entries[0].EventName.ShouldBe("E1");
        entries[0].StreamVersion.ShouldBe(0);
        entries[1].EventName.ShouldBe("E2");
        entries[1].StreamVersion.ShouldBe(1);

        var batchCompleted = await ReadLastBatchCompleted();
        var rejection = batchCompleted.Rejections.First();
        rejection.CommitId.ShouldBe(requests[1].CommitId);
        rejection.StreamId.ShouldBe(streamId);
        rejection.ExpectedStreamState.ShouldBe(ExpectedStreamState.StreamDoesNotExist);
        rejection.ActualStreamState.ShouldBe(StreamState.StreamExists(StreamVersion.FromInt64(0)));
    }

    // Mixed outcomes

    [Test]
    public async Task AppendBatchAsync_MixedAppendsAndRejections_AllCategorisedCorrectly()
    {
        var appender = CreateAppender();
        var streamId = CreateStreamId();

        // Seed the stream.
        var seedCommitId = Guid.CreateVersion7();
        await appender.AppendBatchAsync([
            CreateRequest(seedCommitId, streamId, ExpectedStreamState.Any, "E1")
        ]);

        // Batch with: one good append, one rejection (wrong version), one duplicate (seedCommitId).
        var goodCommitId = Guid.CreateVersion7();
        var badCommitId = Guid.CreateVersion7();

        await appender.AppendBatchAsync([
            CreateRequest(goodCommitId, streamId, ExpectedStreamState.SpecificVersion(new StreamVersion(0)), "E2"),
            CreateRequest(badCommitId, streamId, ExpectedStreamState.StreamDoesNotExist, "Nope"),
            CreateRequest(seedCommitId, streamId, ExpectedStreamState.Any, "E1") // duplicate
        ]);

        var batchCompleted = await ReadLastBatchCompleted();

        batchCompleted.Appends.ShouldBe([goodCommitId]);
        batchCompleted.Rejections.Count.ShouldBe(1);
        batchCompleted.Rejections.First().CommitId.ShouldBe(badCommitId);
        batchCompleted.Duplicates.ShouldBe([seedCommitId]);
    }

    // initialCommitPosition

    [Test]
    public async Task AppendBatchAsync_WithInitialCommitPosition_StartsAtCorrectPosition()
    {
        // Simulate resuming after position 4 (next position should be 5).
        var appender = CreateAppender(initialCommitPosition: 4);

        await appender.AppendBatchAsync([
            CreateRequest(Guid.CreateVersion7(), CreateStreamId(), ExpectedStreamState.Any, "E1")
        ]);

        var entries = await ReadEventEntries();
        entries.Count.ShouldBe(1);
        entries[0].Position.ShouldBe(5);
    }

    private static string CreateStreamId() => $"stream-{Guid.CreateVersion7():N}";

    private EventAppender CreateAppender(long? initialCommitPosition = null)
    {
        return new EventAppender(
            _eventLog,
            Epoch,
            initialCommitPosition,
            _loggerFactory.CreateLogger<EventAppender>());
    }

    private static AppendRequest CreateRequest(
        Guid commitId,
        string streamId,
        ExpectedStreamState expectedState,
        params string[] eventNames)
    {
        var now = DateTime.UtcNow;

        return new AppendRequest
        {
            CommitId = commitId,
            StreamId = streamId,
            ExpectedStreamState = expectedState,
            Events = eventNames
                .Select(name => new PendingEvent
                {
                    EventName = name,
                    EventData = new BsonDocument("value", name),
                    Metadata = BsonNull.Value
                })
                .ToArray(),
            CreatedAtUtc = now,
            LastSeenAtUtc = now
        };
    }

    private async Task<List<EventLogEntry>> ReadEventEntries()
    {
        return await _eventLog
            .Find(Builders<EventLogEntry>.Filter.Ne(x => x.EventName, "AppendBatchCompleted"))
            .SortBy(x => x.Position)
            .ToListAsync();
    }

    private async Task<List<EventLogEntry>> ReadAllEntries()
    {
        return await _eventLog
            .Find(Builders<EventLogEntry>.Filter.Empty)
            .SortBy(x => x.Position)
            .ToListAsync();
    }

    private async Task<AppendBatchCompleted> ReadLastBatchCompleted()
    {
        var entry = await _eventLog
            .Find(Builders<EventLogEntry>.Filter.Eq(x => x.EventName, "AppendBatchCompleted"))
            .SortByDescending(x => x.Position)
            .FirstAsync();

        return BsonSerializer.Deserialize<AppendBatchCompleted>(entry.EventData.AsBsonDocument);
    }
}