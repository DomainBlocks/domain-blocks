using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Coordination;
using DomainBlocks.EventStore.MongoDB.Schema;
using DomainBlocks.Testing.Integration.MongoDB;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration.Coordination;

public class EventLogWriterTests
{
    private const int TestTimeoutMillis = 10_000;
    private const long Epoch = 1;

    private MongoClient _mongoClient = null!;
    private MongoEventStoreNodeOptions _options = null!;
    private ILoggerFactory _loggerFactory = null!;
    private IMongoCollection<EventLogEntry> _eventLog = null!;
    private IMongoCollection<BsonDocument> _eventLogAsBson = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _mongoClient = new MongoClient(MongoConnectionStrings.Default);

        _options = new MongoEventStoreNodeOptions
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
    public async Task WriteAsync_SingleRequest_AppendsEventsWithCorrectPositionsAndVersions(CancellationToken ct)
    {
        var writer = CreateWriter();
        var commitId = Guid.CreateVersion7();
        var streamId = CreateStreamId();

        var requests = new[]
        {
            CreateRequest(commitId, streamId, ExpectedStreamState.Any, "EventA", "EventB", "EventC")
        };

        var result = await writer.WriteAsync(requests, ct);

        result.StartPosition.ShouldBe(0);
        result.Count.ShouldBe(3); // 3 events; no sentinel on happy path

        var entries = await ReadEntries();
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

        result.DuplicatesSkipped.ShouldBeNull();
        result.ConflictsRejected.ShouldBeNull();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task WriteAsync_MultipleRequestsInSingleBatch_AppendsAllWithContiguousPositions(CancellationToken ct)
    {
        var writer = CreateWriter();
        var streamA = CreateStreamId();
        var streamB = CreateStreamId();
        var commitA = Guid.CreateVersion7();
        var commitB = Guid.CreateVersion7();

        var requests = new[]
        {
            CreateRequest(commitA, streamA, ExpectedStreamState.Any, "A1", "A2"),
            CreateRequest(commitB, streamB, ExpectedStreamState.Any, "B1")
        };

        var result = await writer.WriteAsync(requests, ct);

        result.StartPosition.ShouldBe(0);
        result.Count.ShouldBe(3); // 3 events; no sentinel on happy path

        var entries = await ReadEntries();
        entries.Count.ShouldBe(3);

        entries[0].Position.ShouldBe(0);
        entries[0].StreamId.ShouldBe(streamA);
        entries[0].StreamVersion.ShouldBe(0);

        entries[1].Position.ShouldBe(1);
        entries[1].StreamId.ShouldBe(streamA);
        entries[1].StreamVersion.ShouldBe(1);

        entries[2].Position.ShouldBe(2);
        entries[2].StreamId.ShouldBe(streamB);
        entries[2].StreamVersion.ShouldBe(0);

        result.DuplicatesSkipped.ShouldBeNull();
        result.ConflictsRejected.ShouldBeNull();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task WriteAsync_MultipleBatchesOnSameStream_ContinuesPositionAndVersion(CancellationToken ct)
    {
        var writer = CreateWriter();
        var streamId = CreateStreamId();

        BsonDocument[] requests1 =
        [
            CreateRequest(Guid.CreateVersion7(), streamId, ExpectedStreamState.Any, "E1", "E2")
        ];

        BsonDocument[] requests2 =
        [
            CreateRequest(Guid.CreateVersion7(), streamId, ExpectedStreamState.Any, "E3")
        ];

        var result1 = await writer.WriteAsync(requests1, ct);
        var result2 = await writer.WriteAsync(requests2, ct);

        result2.StartPosition.ShouldBe(result1.StartPosition + result1.Count);

        var entries = await ReadEntries();
        entries.Count.ShouldBe(3);

        entries[0].Position.ShouldBe(0);
        entries[0].StreamVersion.ShouldBe(0);

        entries[1].Position.ShouldBe(1);
        entries[1].StreamVersion.ShouldBe(1);

        entries[2].Position.ShouldBe(2); // No extra sentinel position on happy path
        entries[2].StreamVersion.ShouldBe(2);
    }

    // Empty / no-op

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task WriteAsync_EmptyInput_WritesNothing(CancellationToken ct)
    {
        var writer = CreateWriter();

        var result = await writer.WriteAsync([], ct);

        result.ShouldBe(EventLogWriteResult.Empty);

        var all = await ReadAllEntries();
        all.ShouldBeEmpty();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task WriteAsync_RequestWithNoEvents_WritesNothing(CancellationToken ct)
    {
        var writer = CreateWriter();

        var requests = new[]
        {
            CreateRequest(Guid.CreateVersion7(), CreateStreamId(), ExpectedStreamState.Any)
        };

        var result = await writer.WriteAsync(requests, ct);

        result.ShouldBe(EventLogWriteResult.Empty);

        var all = await ReadAllEntries();
        all.ShouldBeEmpty();
    }

    // Duplicate detection (idempotency)

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task WriteAsync_SameCommitIdInTwoSeparateBatches_SecondBatchTreatsDuplicate(CancellationToken ct)
    {
        var writer = CreateWriter();
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

        var result1 = await writer.WriteAsync(requests1, ct);
        var result2 = await writer.WriteAsync(requests2, ct);

        result1.Count.ShouldBe(1); // 1 event; no sentinel on happy path
        result2.Count.ShouldBe(1); // DuplicatesSkipped sentinel only

        // Only one domain event should exist - the duplicate was not re-appended.
        var entries = await ReadEntries();
        entries.Count.ShouldBe(1);

        // First batch: no sentinel.
        result1.DuplicatesSkipped.ShouldBeNull();
        result1.ConflictsRejected.ShouldBeNull();

        // Second batch: DuplicatesSkipped sentinel with the duplicate commit ID.
        result2.DuplicatesSkipped.ShouldNotBeNull();
        DeserializeDuplicatesSkipped(result2.DuplicatesSkipped).CommitIds.ShouldContain(commitId);
        result2.ConflictsRejected.ShouldBeNull();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task WriteAsync_SameCommitIdRepeatedWithinSameBatch_OnlyFirstIsAppended(CancellationToken ct)
    {
        var writer = CreateWriter();
        var commitId = Guid.CreateVersion7();
        var streamId = CreateStreamId();

        var requests = new[]
        {
            CreateRequest(commitId, streamId, ExpectedStreamState.Any, "E1"),
            CreateRequest(commitId, streamId, ExpectedStreamState.Any, "E1")
        };

        var result = await writer.WriteAsync(requests, ct);

        result.Count.ShouldBe(1); // 1 event; within-batch duplicate is silently dropped, no sentinel

        var entries = await ReadEntries();
        entries.Count.ShouldBe(1);
    }

    // OCC: ExpectedStreamState.StreamDoesNotExist

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task WriteAsync_StreamDoesNotExist_AndStreamIsNew_Appends(CancellationToken ct)
    {
        var writer = CreateWriter();
        var commitId = Guid.CreateVersion7();
        var streamId = CreateStreamId();

        BsonDocument[] requests =
        [
            CreateRequest(commitId, streamId, ExpectedStreamState.StreamDoesNotExist, "E1")
        ];

        var result = await writer.WriteAsync(requests, ct);

        result.Count.ShouldBe(1); // 1 event; no sentinel on happy path

        var entries = await ReadEntries();
        entries.Count.ShouldBe(1);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task WriteAsync_StreamDoesNotExist_ButStreamAlreadyExists_Rejects(CancellationToken ct)
    {
        var writer = CreateWriter();
        var streamId = CreateStreamId();

        // First: create the stream.
        await writer.WriteAsync(
            [CreateRequest(Guid.CreateVersion7(), streamId, ExpectedStreamState.Any, "E1")],
            ct);

        // Second: attempt with StreamDoesNotExist - should be rejected.
        var rejectedCommitId = Guid.CreateVersion7();

        var result = await writer.WriteAsync(
            [CreateRequest(rejectedCommitId, streamId, ExpectedStreamState.StreamDoesNotExist, "E2")],
            ct);

        result.Count.ShouldBe(1); // ConflictsRejected sentinel only

        // Only the first event should exist.
        var entries = await ReadEntries();
        entries.Count.ShouldBe(1);

        result.ConflictsRejected.ShouldNotBeNull();
        var conflictsRejected = DeserializeConflictsRejected(result.ConflictsRejected);
        conflictsRejected.Conflicts.Count.ShouldBe(1);
        var conflict = conflictsRejected.Conflicts.First();
        conflict.CommitId.ShouldBe(rejectedCommitId);
        conflict.StreamId.ShouldBe(streamId);
    }

    // OCC: ExpectedStreamState.StreamExists

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task WriteAsync_StreamExists_AndStreamHasEvents_Appends(CancellationToken ct)
    {
        var writer = CreateWriter();
        var streamId = CreateStreamId();

        await writer.WriteAsync(
            [CreateRequest(Guid.CreateVersion7(), streamId, ExpectedStreamState.Any, "E1")],
            ct);

        var commitId = Guid.CreateVersion7();

        var result = await writer.WriteAsync(
            [CreateRequest(commitId, streamId, ExpectedStreamState.StreamExists, "E2")],
            ct);

        result.Count.ShouldBe(1); // 1 event; no sentinel on happy path

        var entries = await ReadEntries();
        entries.Count.ShouldBe(2);
        entries[1].EventName.ShouldBe("E2");
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task WriteAsync_StreamExists_ButStreamIsNew_Rejects(CancellationToken ct)
    {
        var writer = CreateWriter();
        var rejectedCommitId = Guid.CreateVersion7();
        var streamId = CreateStreamId();

        var result = await writer.WriteAsync(
            [CreateRequest(rejectedCommitId, streamId, ExpectedStreamState.StreamExists, "E1")],
            ct);

        result.Count.ShouldBe(1); // ConflictsRejected sentinel only

        var entries = await ReadEntries();
        entries.ShouldBeEmpty();

        result.ConflictsRejected.ShouldNotBeNull();
        var conflictsRejected = DeserializeConflictsRejected(result.ConflictsRejected);
        conflictsRejected.Conflicts.Count.ShouldBe(1);
        var conflict = conflictsRejected.Conflicts.First();
        conflict.CommitId.ShouldBe(rejectedCommitId);
        conflict.StreamId.ShouldBe(streamId);
    }

    // OCC: ExpectedStreamState.SpecificVersion

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task WriteAsync_SpecificVersion_MatchesActual_Appends(CancellationToken ct)
    {
        var writer = CreateWriter();
        var streamId = CreateStreamId();

        // Append two events - stream at version 1.
        await writer.WriteAsync(
            [CreateRequest(Guid.CreateVersion7(), streamId, ExpectedStreamState.Any, "E1", "E2")],
            ct);

        // Expect version 1 - should succeed.
        var commitId = Guid.CreateVersion7();

        var result = await writer.WriteAsync(
            [CreateRequest(commitId, streamId, ExpectedStreamState.SpecificVersion(new StreamVersion(1)), "E3")],
            ct);

        result.Count.ShouldBe(1); // 1 event; no sentinel on happy path

        var entries = await ReadEntries();
        entries.Count.ShouldBe(3);
        entries[2].EventName.ShouldBe("E3");
        entries[2].StreamVersion.ShouldBe(2);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task WriteAsync_SpecificVersion_DoesNotMatchActual_Rejects(CancellationToken ct)
    {
        var writer = CreateWriter();
        var streamId = CreateStreamId();

        // Append two events - stream at version 1.
        await writer.WriteAsync(
            [CreateRequest(Guid.CreateVersion7(), streamId, ExpectedStreamState.Any, "E1", "E2")],
            ct);

        // Expect version 0 (stale) - should be rejected.
        var rejectedCommitId = Guid.CreateVersion7();

        var result = await writer.WriteAsync(
            [
                CreateRequest(
                    rejectedCommitId,
                    streamId,
                    ExpectedStreamState.SpecificVersion(new StreamVersion(0)),
                    "E3")
            ],
            ct);

        result.Count.ShouldBe(1); // ConflictsRejected sentinel only

        var entries = await ReadEntries();
        entries.Count.ShouldBe(2); // Only the original two.

        result.ConflictsRejected.ShouldNotBeNull();
        var conflict = DeserializeConflictsRejected(result.ConflictsRejected).Conflicts.First();
        conflict.CommitId.ShouldBe(rejectedCommitId);
        conflict.StreamId.ShouldBe(streamId);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task WriteAsync_SpecificVersion_StreamDoesNotExist_Rejects(CancellationToken ct)
    {
        var writer = CreateWriter();
        var streamId = CreateStreamId();
        var rejectedCommitId = Guid.CreateVersion7();

        var result = await writer.WriteAsync(
            [
                CreateRequest(
                    rejectedCommitId,
                    streamId,
                    ExpectedStreamState.SpecificVersion(new StreamVersion(0)),
                    "E1")
            ],
            ct);

        result.Count.ShouldBe(1); // ConflictsRejected sentinel only

        var entries = await ReadEntries();
        entries.ShouldBeEmpty();

        result.ConflictsRejected.ShouldNotBeNull();
        var conflict = DeserializeConflictsRejected(result.ConflictsRejected).Conflicts.First();
        conflict.CommitId.ShouldBe(rejectedCommitId);
        conflict.StreamId.ShouldBe(streamId);
    }

    // OCC within a single batch

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task WriteAsync_TwoRequestsForSameStream_SecondSeesFirstsVersion(CancellationToken ct)
    {
        var writer = CreateWriter();
        var streamId = CreateStreamId();

        var requests = new[]
        {
            CreateRequest(Guid.CreateVersion7(), streamId, ExpectedStreamState.StreamDoesNotExist, "E1"),
            CreateRequest(Guid.CreateVersion7(), streamId, ExpectedStreamState.StreamExists, "E2")
        };

        var result = await writer.WriteAsync(requests, ct);

        result.Count.ShouldBe(2); // 2 events; no sentinel on happy path

        var entries = await ReadEntries();
        entries.Count.ShouldBe(2);
        entries[0].StreamVersion.ShouldBe(0);
        entries[1].StreamVersion.ShouldBe(1);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task WriteAsync_RejectionDoesNotAdvanceStreamVersion(CancellationToken ct)
    {
        var writer = CreateWriter();
        var streamId = CreateStreamId();
        var rejectedCommitId = Guid.CreateVersion7();

        var requests = new[]
        {
            CreateRequest(Guid.CreateVersion7(), streamId, ExpectedStreamState.Any, "E1"),
            CreateRequest(rejectedCommitId, streamId, ExpectedStreamState.StreamDoesNotExist, "Rejected"),
            CreateRequest(
                Guid.CreateVersion7(),
                streamId,
                ExpectedStreamState.SpecificVersion(new StreamVersion(0)),
                "E2")
        };

        var result = await writer.WriteAsync(requests, ct);

        result.Count.ShouldBe(3); // 2 events + ConflictsRejected sentinel

        var entries = await ReadEntries();
        entries.Count.ShouldBe(2);
        entries[0].EventName.ShouldBe("E1");
        entries[0].StreamVersion.ShouldBe(0);
        entries[1].EventName.ShouldBe("E2");
        entries[1].StreamVersion.ShouldBe(1);

        result.ConflictsRejected.ShouldNotBeNull();
        var conflict = DeserializeConflictsRejected(result.ConflictsRejected).Conflicts.First();
        conflict.CommitId.ShouldBe(rejectedCommitId);
        conflict.StreamId.ShouldBe(streamId);
    }

    // Mixed outcomes

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task WriteAsync_MixedAppendsAndRejections_AllCategorisedCorrectly(CancellationToken ct)
    {
        var writer = CreateWriter();
        var streamId = CreateStreamId();

        // Seed the stream.
        var seedCommitId = Guid.CreateVersion7();

        await writer.WriteAsync(
            [CreateRequest(seedCommitId, streamId, ExpectedStreamState.Any, "E1")],
            ct);

        // Batch with: one good append, one conflict (wrong version), one duplicate (seedCommitId).
        var goodCommitId = Guid.CreateVersion7();
        var badCommitId = Guid.CreateVersion7();

        var result = await writer.WriteAsync(
            [
                CreateRequest(goodCommitId, streamId, ExpectedStreamState.SpecificVersion(new StreamVersion(0)), "E2"),
                CreateRequest(badCommitId, streamId, ExpectedStreamState.StreamDoesNotExist, "Nope"),
                CreateRequest(seedCommitId, streamId, ExpectedStreamState.Any, "E1") // duplicate
            ],
            ct);

        result.Count.ShouldBe(3); // 1 event + DuplicatesSkipped + ConflictsRejected

        var entries = await ReadEntries();
        entries.Count.ShouldBe(2); // E1 (seed) + E2 (good)
        entries[1].CommitId.ShouldBe(goodCommitId);

        result.DuplicatesSkipped.ShouldNotBeNull();
        DeserializeDuplicatesSkipped(result.DuplicatesSkipped).CommitIds.ShouldContain(seedCommitId);

        result.ConflictsRejected.ShouldNotBeNull();
        var conflictsRejected = DeserializeConflictsRejected(result.ConflictsRejected);
        conflictsRejected.Conflicts.Count.ShouldBe(1);
        conflictsRejected.Conflicts.First().CommitId.ShouldBe(badCommitId);
    }

    // initialCommitPosition

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task WriteAsync_WithInitialCommitPosition_StartsAtCorrectPosition(CancellationToken ct)
    {
        // Simulate resuming after position 4 (next position should be 5).
        var writer = CreateWriter(initialCommitPosition: 4);

        var result = await writer.WriteAsync(
            [CreateRequest(Guid.CreateVersion7(), CreateStreamId(), ExpectedStreamState.Any, "E1")],
            ct);

        result.StartPosition.ShouldBe(5);
        result.Count.ShouldBe(1); // 1 event; no sentinel on happy path

        var entries = await ReadEntries();
        entries.Count.ShouldBe(1);
        entries[0].Position.ShouldBe(5);
    }

    // Epoch fencing

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task WriteAsync_HigherEpoch_OverwritesLowerEpochEntries(CancellationToken ct)
    {
        // Seed two events at epoch 1, occupying positions 0 and 1.
        await SeedEventLogEntry(position: 0, epoch: 1);
        await SeedEventLogEntry(position: 1, epoch: 1);

        // Writer at epoch 2 targets the same positions - should overwrite both.
        var writer = CreateWriter(epoch: 2);
        var commitId = Guid.CreateVersion7();

        var result = await writer.WriteAsync(
            [CreateRequest(commitId, CreateStreamId(), ExpectedStreamState.Any, "New1", "New2")],
            ct);

        result.StartPosition.ShouldBe(0);
        result.Count.ShouldBe(2); // 2 events; no sentinel on happy path

        var entries = await ReadEntries();
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
    public async Task WriteAsync_SameEpoch_CannotOverwriteExistingEntries(CancellationToken ct)
    {
        // Seed position 0 at epoch 2.
        await SeedEventLogEntry(position: 0, epoch: 2);

        // Writer also at epoch 2 - guard requires epoch < 2, won't match.
        var writer = CreateWriter(epoch: 2);

        await writer
            .WriteAsync(
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
    public async Task WriteAsync_LowerEpoch_CannotOverwriteHigherEpochEntries(CancellationToken ct)
    {
        // Seed position 0 at epoch 3.
        await SeedEventLogEntry(position: 0, epoch: 3);

        // Writer at epoch 2 - guard requires epoch < 2, won't match epoch 3.
        var writer = CreateWriter(epoch: 2);

        await writer
            .WriteAsync(
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

    private EventLogWriter CreateWriter(long epoch = Epoch, long? initialCommitPosition = null)
    {
        return new EventLogWriter(
            _eventLogAsBson,
            epoch,
            initialCommitPosition,
            _loggerFactory.CreateLogger<EventLogWriter>());
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
            CommitIndex = 0,
            EventName = "SeededEvent",
            EventData = BsonNull.Value,
            Metadata = BsonNull.Value,
            WrittenAtUtc = DateTime.UtcNow
        });
    }

    private async Task<List<EventLogEntry>> ReadEntries()
    {
        return await _eventLog
            .Find(Builders<EventLogEntry>.Filter.Nin(
                x => x.EventName,
                [EventNames.DuplicatesSkipped, EventNames.ConflictsRejected]))
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

    private static DuplicatesSkipped DeserializeDuplicatesSkipped(BsonValue payload) =>
        BsonSerializer.Deserialize<DuplicatesSkipped>(payload.AsBsonDocument);

    private static ConflictsRejected DeserializeConflictsRejected(BsonValue payload) =>
        BsonSerializer.Deserialize<ConflictsRejected>(payload.AsBsonDocument);
}