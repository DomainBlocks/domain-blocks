using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Client.Coordination;
using DomainBlocks.EventStore.MongoDB.Client.Schema;
using DomainBlocks.Testing.Integration.MongoDB;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.MongoDB.Client.Tests.Integration.Coordination;

public class EventLogAppenderTests
{
    private const int TestTimeoutMillis = 10_000;
    private const long Epoch = 1;

    private MongoClient _mongoClient = null!;
    private MongoEventStoreOptions _options = null!;
    private ILoggerFactory _loggerFactory = null!;
    private IMongoCollection<EventLogEntry> _eventLog = null!;
    private IMongoCollection<BsonDocument> _eventLogAsBson = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _mongoClient = new MongoClient(MongoConnectionStrings.Default);

        _options = new MongoEventStoreOptions
        {
            DatabaseName = "domainblocks_tests"
        };

        _loggerFactory = LoggerFactory.Create(x => x.AddConsole().SetMinimumLevel(LogLevel.Debug));

        await MongoEventStoreAdmin.EnsureInitializedAsync(_mongoClient, _options);

        var db = _mongoClient.GetDatabase(_options.DatabaseName);
        _eventLog = db.GetCollection<EventLogEntry>(_options.EventLogCollectionName);
        _eventLogAsBson = db.GetCollection<BsonDocument>(_options.EventLogCollectionName);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _eventLog.DeleteManyAsync(Builders<EventLogEntry>.Filter.Empty);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _mongoClient.DropDatabaseAsync(_options.DatabaseName);
        _mongoClient.Dispose();
        _loggerFactory.Dispose();
    }

    // Basic append

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendBatchAsync_SingleRequest_AppendsEventsWithCorrectPositionsAndVersions(CancellationToken ct)
    {
        var appender = CreateAppender();
        var commitId = Guid.CreateVersion7();
        var streamId = CreateStreamId();

        var requests = new[]
        {
            CreateRequest(commitId, streamId, ExpectedStreamState.Any, "EventA", "EventB", "EventC")
        };

        var result = await appender.AppendBatchAsync(requests, ct);

        result.StartPosition.ShouldBe(0);
        result.NextPosition.ShouldBe(4);
        result.Count.ShouldBe(4);
        result.IsEmpty.ShouldBeFalse();

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
        batchCompleted.AppendedCommitIds.ShouldBe([commitId]);
        batchCompleted.DuplicateCommitIds.ShouldBeEmpty();
        batchCompleted.Rejections.ShouldBeEmpty();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendBatchAsync_MultipleRequestsInSingleBatch_AppendsAllWithContiguousPositions(
        CancellationToken ct)
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

        var result = await appender.AppendBatchAsync(requests, ct);

        result.StartPosition.ShouldBe(0);
        result.NextPosition.ShouldBe(4);
        result.Count.ShouldBe(4);
        result.IsEmpty.ShouldBeFalse();

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
        batchCompleted.AppendedCommitIds.ShouldContain(commitA);
        batchCompleted.AppendedCommitIds.ShouldContain(commitB);
        batchCompleted.DuplicateCommitIds.ShouldBeEmpty();
        batchCompleted.Rejections.ShouldBeEmpty();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendBatchAsync_MultipleBatchesOnSameStream_ContinuesPositionAndVersion(CancellationToken ct)
    {
        var appender = CreateAppender();
        var streamId = CreateStreamId();

        BsonDocument[] requests1 =
        [
            CreateRequest(Guid.CreateVersion7(), streamId, ExpectedStreamState.Any, "E1", "E2")
        ];

        BsonDocument[] requests2 =
        [
            CreateRequest(Guid.CreateVersion7(), streamId, ExpectedStreamState.Any, "E3")
        ];

        var result1 = await appender.AppendBatchAsync(requests1, ct);
        var result2 = await appender.AppendBatchAsync(requests2, ct);

        result2.StartPosition.ShouldBe(result1.NextPosition);

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
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendBatchAsync_EmptyInput_WritesNothing(CancellationToken ct)
    {
        var appender = CreateAppender();

        var result = await appender.AppendBatchAsync([], ct);

        result.IsEmpty.ShouldBeTrue();

        var all = await ReadAllEntries();
        all.ShouldBeEmpty();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendBatchAsync_RequestWithNoEvents_WritesNothing(CancellationToken ct)
    {
        var appender = CreateAppender();

        var requests = new[]
        {
            CreateRequest(Guid.CreateVersion7(), CreateStreamId(), ExpectedStreamState.Any)
        };

        var result = await appender.AppendBatchAsync(requests, ct);

        result.IsEmpty.ShouldBeTrue();

        var all = await ReadAllEntries();
        all.ShouldBeEmpty();
    }

    // Duplicate detection (idempotency)

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendBatchAsync_SameCommitIdInTwoSeparateBatches_SecondBatchTreatsDuplicate(CancellationToken ct)
    {
        var appender = CreateAppender();
        var commitId = Guid.CreateVersion7();
        var streamId = CreateStreamId();

        BsonDocument[] requests1 =
        [
            CreateRequest(commitId, streamId, ExpectedStreamState.Any, "E1")
        ];

        BsonDocument[] requests2 =
        [
            CreateRequest(commitId, streamId, ExpectedStreamState.Any, "E1")
        ];

        var result1 = await appender.AppendBatchAsync(requests1, ct);
        var result2 = await appender.AppendBatchAsync(requests2, ct);

        result1.Count.ShouldBe(2);
        result2.Count.ShouldBe(1); // Marker only

        // Only one event should exist - the duplicate wasn't re-appended.
        var entries = await ReadEventEntries();
        entries.Count.ShouldBe(1);

        // Second batch should still produce a batch-completed marker with the duplicate.
        var allEntries = await ReadAllEntries();
        var batchCompletedEntries = allEntries.Where(e => e.EventName == EventNames.AppendBatchRecorded).ToArray();
        batchCompletedEntries.Length.ShouldBe(2);

        var secondMarker = batchCompletedEntries.OrderBy(e => e.Position).Last();
        var secondCompleted = BsonSerializer.Deserialize<AppendBatchRecorded>(secondMarker.EventData.AsBsonDocument);
        secondCompleted.DuplicateCommitIds.ShouldContain(commitId);
        secondCompleted.AppendedCommitIds.ShouldBeEmpty();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendBatchAsync_SameCommitIdRepeatedWithinSameBatch_OnlyFirstIsAppended(CancellationToken ct)
    {
        var appender = CreateAppender();
        var commitId = Guid.CreateVersion7();
        var streamId = CreateStreamId();

        var requests = new[]
        {
            CreateRequest(commitId, streamId, ExpectedStreamState.Any, "E1"),
            CreateRequest(commitId, streamId, ExpectedStreamState.Any, "E1")
        };

        var result = await appender.AppendBatchAsync(requests, ct);

        result.Count.ShouldBe(2);

        var entries = await ReadEventEntries();
        entries.Count.ShouldBe(1);
    }

    // OCC: ExpectedStreamState.StreamDoesNotExist

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendBatchAsync_StreamDoesNotExist_AndStreamIsNew_Appends(CancellationToken ct)
    {
        var appender = CreateAppender();
        var commitId = Guid.CreateVersion7();
        var streamId = CreateStreamId();

        BsonDocument[] requests =
        [
            CreateRequest(commitId, streamId, ExpectedStreamState.StreamDoesNotExist, "E1")
        ];

        var result = await appender.AppendBatchAsync(requests, ct);

        result.Count.ShouldBe(2);

        var entries = await ReadEventEntries();
        entries.Count.ShouldBe(1);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendBatchAsync_StreamDoesNotExist_ButStreamAlreadyExists_Rejects(CancellationToken ct)
    {
        var appender = CreateAppender();
        var streamId = CreateStreamId();

        // First: create the stream.
        await appender.AppendBatchAsync(
            [CreateRequest(Guid.CreateVersion7(), streamId, ExpectedStreamState.Any, "E1")],
            ct);

        // Second: attempt with StreamDoesNotExist - should be rejected.
        var rejectedCommitId = Guid.CreateVersion7();

        var result = await appender.AppendBatchAsync(
            [CreateRequest(rejectedCommitId, streamId, ExpectedStreamState.StreamDoesNotExist, "E2")],
            ct);

        result.Count.ShouldBe(1);

        // Only the first event should exist.
        var entries = await ReadEventEntries();
        entries.Count.ShouldBe(1);

        var batchCompleted = await ReadLastBatchCompleted();
        batchCompleted.Rejections.Count.ShouldBe(1);

        var rejection = batchCompleted.Rejections.First();
        rejection.CommitId.ShouldBe(rejectedCommitId);
        rejection.StreamId.ShouldBe(streamId);
    }

    // OCC: ExpectedStreamState.StreamExists

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendBatchAsync_StreamExists_AndStreamHasEvents_Appends(CancellationToken ct)
    {
        var appender = CreateAppender();
        var streamId = CreateStreamId();

        await appender.AppendBatchAsync(
            [CreateRequest(Guid.CreateVersion7(), streamId, ExpectedStreamState.Any, "E1")],
            ct);

        var commitId = Guid.CreateVersion7();

        var result = await appender.AppendBatchAsync(
            [CreateRequest(commitId, streamId, ExpectedStreamState.StreamExists, "E2")],
            ct);

        result.Count.ShouldBe(2);

        var entries = await ReadEventEntries();
        entries.Count.ShouldBe(2);
        entries[1].EventName.ShouldBe("E2");
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendBatchAsync_StreamExists_ButStreamIsNew_Rejects(CancellationToken ct)
    {
        var appender = CreateAppender();
        var rejectedCommitId = Guid.CreateVersion7();
        var streamId = CreateStreamId();

        var result = await appender.AppendBatchAsync(
            [CreateRequest(rejectedCommitId, streamId, ExpectedStreamState.StreamExists, "E1")],
            ct);

        result.Count.ShouldBe(1);

        var entries = await ReadEventEntries();
        entries.ShouldBeEmpty();

        var batchCompleted = await ReadLastBatchCompleted();
        batchCompleted.Rejections.Count.ShouldBe(1);

        var rejection = batchCompleted.Rejections.First();
        rejection.CommitId.ShouldBe(rejectedCommitId);
        rejection.StreamId.ShouldBe(streamId);
    }

    // OCC: ExpectedStreamState.SpecificVersion

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendBatchAsync_SpecificVersion_MatchesActual_Appends(CancellationToken ct)
    {
        var appender = CreateAppender();
        var streamId = CreateStreamId();

        // Append two events → stream at version 1.
        await appender.AppendBatchAsync(
            [CreateRequest(Guid.CreateVersion7(), streamId, ExpectedStreamState.Any, "E1", "E2")],
            ct);

        // Expect version 1 - should succeed.
        var commitId = Guid.CreateVersion7();

        var result = await appender.AppendBatchAsync(
            [CreateRequest(commitId, streamId, ExpectedStreamState.SpecificVersion(new StreamVersion(1)), "E3")],
            ct);

        result.Count.ShouldBe(2);

        var entries = await ReadEventEntries();
        entries.Count.ShouldBe(3);
        entries[2].EventName.ShouldBe("E3");
        entries[2].StreamVersion.ShouldBe(2);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendBatchAsync_SpecificVersion_DoesNotMatchActual_Rejects(CancellationToken ct)
    {
        var appender = CreateAppender();
        var streamId = CreateStreamId();

        // Append two events → stream at version 1.
        await appender.AppendBatchAsync(
            [CreateRequest(Guid.CreateVersion7(), streamId, ExpectedStreamState.Any, "E1", "E2")],
            ct);

        // Expect version 0 (stale) - should be rejected.
        var rejectedCommitId = Guid.CreateVersion7();

        var result = await appender.AppendBatchAsync(
            [
                CreateRequest(
                    rejectedCommitId,
                    streamId,
                    ExpectedStreamState.SpecificVersion(new StreamVersion(0)),
                    "E3")
            ],
            ct);

        result.Count.ShouldBe(1);

        var entries = await ReadEventEntries();
        entries.Count.ShouldBe(2); // Only the original two.

        var batchCompleted = await ReadLastBatchCompleted();
        var rejection = batchCompleted.Rejections.First();
        rejection.CommitId.ShouldBe(rejectedCommitId);
        rejection.StreamId.ShouldBe(streamId);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendBatchAsync_SpecificVersion_StreamDoesNotExist_Rejects(CancellationToken ct)
    {
        var appender = CreateAppender();
        var streamId = CreateStreamId();
        var rejectedCommitId = Guid.CreateVersion7();

        var result = await appender.AppendBatchAsync(
            [
                CreateRequest(
                    rejectedCommitId,
                    streamId,
                    ExpectedStreamState.SpecificVersion(new StreamVersion(0)),
                    "E1")
            ],
            ct);

        result.Count.ShouldBe(1);

        var entries = await ReadEventEntries();
        entries.ShouldBeEmpty();

        var batchCompleted = await ReadLastBatchCompleted();
        var rejection = batchCompleted.Rejections.First();
        rejection.CommitId.ShouldBe(rejectedCommitId);
        rejection.StreamId.ShouldBe(streamId);
    }

    // OCC within a single batch

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendBatchAsync_TwoRequestsForSameStream_SecondSeesFirstsVersion(CancellationToken ct)
    {
        var appender = CreateAppender();
        var streamId = CreateStreamId();

        var requests = new[]
        {
            CreateRequest(Guid.CreateVersion7(), streamId, ExpectedStreamState.StreamDoesNotExist, "E1"),
            CreateRequest(Guid.CreateVersion7(), streamId, ExpectedStreamState.StreamExists, "E2")
        };

        var result = await appender.AppendBatchAsync(requests, ct);

        result.Count.ShouldBe(3);

        var entries = await ReadEventEntries();
        entries.Count.ShouldBe(2);
        entries[0].StreamVersion.ShouldBe(0);
        entries[1].StreamVersion.ShouldBe(1);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendBatchAsync_RejectionDoesNotAdvanceStreamVersion(CancellationToken ct)
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

        var result = await appender.AppendBatchAsync(requests, ct);

        result.Count.ShouldBe(3);

        var entries = await ReadEventEntries();
        entries.Count.ShouldBe(2);
        entries[0].EventName.ShouldBe("E1");
        entries[0].StreamVersion.ShouldBe(0);
        entries[1].EventName.ShouldBe("E2");
        entries[1].StreamVersion.ShouldBe(1);

        var batchCompleted = await ReadLastBatchCompleted();
        var rejection = batchCompleted.Rejections.First();
        rejection.CommitId.ShouldBe(requests[1][CommitRejection.FieldNames.CommitId].AsGuid);
        rejection.StreamId.ShouldBe(streamId);
    }

    // Mixed outcomes

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendBatchAsync_MixedAppendsAndRejections_AllCategorisedCorrectly(CancellationToken ct)
    {
        var appender = CreateAppender();
        var streamId = CreateStreamId();

        // Seed the stream.
        var seedCommitId = Guid.CreateVersion7();

        await appender.AppendBatchAsync(
            [CreateRequest(seedCommitId, streamId, ExpectedStreamState.Any, "E1")],
            ct);

        // Batch with: one good append, one rejection (wrong version), one duplicate (seedCommitId).
        var goodCommitId = Guid.CreateVersion7();
        var badCommitId = Guid.CreateVersion7();

        var result = await appender.AppendBatchAsync(
            [
                CreateRequest(goodCommitId, streamId, ExpectedStreamState.SpecificVersion(new StreamVersion(0)), "E2"),
                CreateRequest(badCommitId, streamId, ExpectedStreamState.StreamDoesNotExist, "Nope"),
                CreateRequest(seedCommitId, streamId, ExpectedStreamState.Any, "E1") // duplicate
            ],
            ct);

        result.Count.ShouldBe(2); // 1 event + marker

        var batchCompleted = await ReadLastBatchCompleted();
        batchCompleted.AppendedCommitIds.ShouldBe([goodCommitId]);
        batchCompleted.Rejections.Count.ShouldBe(1);
        batchCompleted.Rejections.First().CommitId.ShouldBe(badCommitId);
        batchCompleted.DuplicateCommitIds.ShouldBe([seedCommitId]);
    }

    // initialCommitPosition

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendBatchAsync_WithInitialCommitPosition_StartsAtCorrectPosition(CancellationToken ct)
    {
        // Simulate resuming after position 4 (next position should be 5).
        var appender = CreateAppender(initialCommitPosition: 4);

        var result = await appender.AppendBatchAsync(
            [CreateRequest(Guid.CreateVersion7(), CreateStreamId(), ExpectedStreamState.Any, "E1")],
            ct);

        result.StartPosition.ShouldBe(5);
        result.NextPosition.ShouldBe(7);
        result.Count.ShouldBe(2);

        var entries = await ReadEventEntries();
        entries.Count.ShouldBe(1);
        entries[0].Position.ShouldBe(5);
    }

    // Epoch fencing

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendBatchAsync_HigherEpoch_OverwritesLowerEpochEntries(CancellationToken ct)
    {
        // Seed two events at epoch 1, occupying positions 0 and 1.
        await SeedEventLogEntry(position: 0, epoch: 1);
        await SeedEventLogEntry(position: 1, epoch: 1);

        // Appender at epoch 2 targets the same positions - should overwrite both.
        var appender = CreateAppender(epoch: 2);
        var commitId = Guid.CreateVersion7();

        var result = await appender.AppendBatchAsync(
            [CreateRequest(commitId, CreateStreamId(), ExpectedStreamState.Any, "New1", "New2")],
            ct);

        result.StartPosition.ShouldBe(0);
        result.NextPosition.ShouldBe(3); // 2 events + 1 marker
        result.Count.ShouldBe(3);

        var entries = await ReadEventEntries();
        entries.Count.ShouldBe(2);
        entries[0].Position.ShouldBe(0);
        entries[0].Epoch.ShouldBe(2);
        entries[0].EventName.ShouldBe("New1");
        entries[1].Position.ShouldBe(1);
        entries[1].Epoch.ShouldBe(2);
        entries[1].EventName.ShouldBe("New2");
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendBatchAsync_SameEpoch_CannotOverwriteExistingEntries(CancellationToken ct)
    {
        // Seed position 0 at epoch 2.
        await SeedEventLogEntry(position: 0, epoch: 2);

        // Appender also at epoch 2 - guard requires epoch < 2, won't match.
        var appender = CreateAppender(epoch: 2);

        await appender
            .AppendBatchAsync(
                [CreateRequest(Guid.CreateVersion7(), CreateStreamId(), ExpectedStreamState.Any, "E1")],
                ct)
            .ShouldThrowAsync<MongoBulkWriteException<BsonDocument>>();

        // Original entry untouched.
        var entries = await ReadAllEntries();
        entries.Count.ShouldBe(1);
        entries[0].Position.ShouldBe(0);
        entries[0].Epoch.ShouldBe(2);
        entries[0].EventName.ShouldBe("SeededEvent");
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendBatchAsync_LowerEpoch_CannotOverwriteHigherEpochEntries(CancellationToken ct)
    {
        // Seed position 0 at epoch 3.
        await SeedEventLogEntry(position: 0, epoch: 3);

        // Appender at epoch 2 - guard requires epoch < 2, won't match epoch 3.
        var appender = CreateAppender(epoch: 2);

        await appender
            .AppendBatchAsync(
                [CreateRequest(Guid.CreateVersion7(), CreateStreamId(), ExpectedStreamState.Any, "E1")],
                ct)
            .ShouldThrowAsync<MongoBulkWriteException<BsonDocument>>();

        // Original entry untouched.
        var entries = await ReadAllEntries();
        entries.Count.ShouldBe(1);
        entries[0].Position.ShouldBe(0);
        entries[0].Epoch.ShouldBe(3);
        entries[0].EventName.ShouldBe("SeededEvent");
    }

    private static string CreateStreamId() => $"stream-{Guid.CreateVersion7():N}";

    private EventLogAppender CreateAppender(long epoch = Epoch, long? initialCommitPosition = null)
    {
        return new EventLogAppender(
            _eventLogAsBson,
            epoch,
            initialCommitPosition,
            _loggerFactory.CreateLogger<EventLogAppender>());
    }

    private static BsonDocument CreateRequest(
        Guid commitId,
        string streamId,
        ExpectedStreamState expectedState,
        params string[] eventNames)
    {
        var now = DateTime.UtcNow;

        var request = new AppendRequest
        {
            CommitId = commitId,
            StreamId = streamId,
            ExpectedStreamState = expectedState,
            Events =
            [
                .. eventNames
                    .Select(name => new PendingEvent
                    {
                        EventName = name,
                        EventData = new BsonDocument("value", name),
                        Metadata = BsonNull.Value
                    })
            ],
            CreatedAtUtc = now
        };

        return request.ToBsonDocument();
    }

    private async Task SeedEventLogEntry(long position, long epoch)
    {
        await _eventLog.InsertOneAsync(new EventLogEntry
        {
            Position = position,
            Epoch = epoch,
            StreamId = CreateStreamId(),
            StreamVersion = 0,
            CommitId = Guid.CreateVersion7(),
            EventName = "SeededEvent",
            EventData = BsonNull.Value,
            Metadata = BsonNull.Value,
            WrittenAtUtc = DateTime.UtcNow
        });
    }

    private async Task<List<EventLogEntry>> ReadEventEntries()
    {
        return await _eventLog
            .Find(Builders<EventLogEntry>.Filter.Ne(x => x.EventName, EventNames.AppendBatchRecorded))
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

    private async Task<AppendBatchRecorded> ReadLastBatchCompleted()
    {
        var entry = await _eventLog
            .Find(Builders<EventLogEntry>.Filter.Eq(x => x.EventName, EventNames.AppendBatchRecorded))
            .SortByDescending(x => x.Position)
            .FirstAsync();

        return BsonSerializer.Deserialize<AppendBatchRecorded>(entry.EventData.AsBsonDocument);
    }
}