using DomainBlocks.Testing.Integration.EventStore;
using DomainBlocks.Testing.Integration.EventStore.MongoDB;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Events;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

/// <summary>
/// Tests that a snapshot's read of the end waits for the mark that it is given, and that its reads of the events wait
/// for the operation time of that read, so they can't see less than the end it found.
/// </summary>
[TestFixture]
public class MongoCatchUpReaderTests
{
    private const int InvalidOptionsCode = 72;

    private static readonly BsonTimestamp EarliestMark = new(1, 1);
    private static readonly BsonTimestamp MarkAheadOfCluster = new(int.MaxValue, 1);

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadSnapshotAsync_EventsInSequence_ResumesAfterEnd(CancellationToken ct)
    {
        await using var scenario = await CatchUpScenario.StartAsync(ct);
        await scenario.AppendAsync("existing", 2);
        var reader = scenario.CreateCatchUpReader(Builders<BsonDocument>.Filter.Empty);

        var snapshot = await reader.ReadSnapshotAsync(EarliestMark, "sub-1", ct);

        snapshot.GetResumePosition(SubscriptionOrigin.Start).ShouldBe(LogPosition.FromInt64(1));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadSnapshotAsync_EmptySequence_ResumesFromStart(CancellationToken ct)
    {
        await using var scenario = await CatchUpScenario.StartAsync(ct);
        await scenario.AppendAsync("existing", 2);
        var noEvents = Builders<BsonDocument>.Filter.Eq(EventLogEntry.FieldNames.StreamId, "no-such-stream");
        var reader = scenario.CreateCatchUpReader(noEvents);

        var snapshot = await reader.ReadSnapshotAsync(EarliestMark, "sub-1", ct);

        snapshot.GetResumePosition(SubscriptionOrigin.Start).ShouldBeNull();
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadSnapshotAsync_MarkAheadOfCluster_Throws(CancellationToken ct)
    {
        await using var scenario = await CatchUpScenario.StartAsync(ct);
        var reader = scenario.CreateCatchUpReader(Builders<BsonDocument>.Filter.Empty);

        var exception = await Should.ThrowAsync<MongoCommandException>(async () =>
            await reader.ReadSnapshotAsync(MarkAheadOfCluster, "sub-1", ct));

        exception.Code.ShouldBe(InvalidOptionsCode);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadAsync_AfterSnapshot_WaitsForOperationTimeOfEndRead(CancellationToken ct)
    {
        await using var scenario = await CatchUpScenario.StartAsync(ct);
        var deliveredClusterTime = await AppendLiveEventAsync(scenario);

        var waitedForTimes = new List<BsonTimestamp>();
        var settings = MongoClientSettings.FromConnectionString(MongoTestEnvironment.ConnectionString);

        settings.ClusterConfigurator = builder => builder.Subscribe<CommandStartedEvent>(e =>
        {
            if (e.CommandName == "find")
                waitedForTimes.Add(e.Command["readConcern"]["afterClusterTime"].AsBsonTimestamp);
        });

        using var client = new MongoClient(settings);
        var reader = scenario.CreateCatchUpReader(Builders<BsonDocument>.Filter.Empty, client);

        var snapshot = await reader.ReadSnapshotAsync(EarliestMark, "sub-1", ct);

        await foreach (var _ in snapshot.ReadAsync(SubscriptionOrigin.Start, ct))
        {
        }

        waitedForTimes.Count.ShouldBe(2);
        waitedForTimes[0].ShouldBe(EarliestMark);
        waitedForTimes[1].ShouldBeGreaterThanOrEqualTo(deliveredClusterTime);
    }

    private static async Task<BsonTimestamp> AppendLiveEventAsync(CatchUpScenario scenario)
    {
        await using var subscription = scenario.SubscribeToAll();

        (await subscription.NextAsync()).IsCaughtUp.ShouldBeTrue();

        await scenario.AppendLiveAsync("live", 1);

        return scenario.ChangeStreamProbe.LastDeliveredClusterTime.ShouldNotBeNull();
    }
}