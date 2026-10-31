using DomainBlocks.Testing.Integration.EventStore;
using DomainBlocks.Testing.Integration.EventStore.MongoDB;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Events;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

/// <summary>
/// Tests that a catch-up reader's read of the end waits for the mark that it is given, and that the catch-up's reads of
/// the events wait for the operation time of that read, so they can't see less than the end it found.
/// </summary>
[TestFixture]
public class MongoCatchUpReaderTests
{
    private const int InvalidOptionsCode = 72;

    private static readonly BsonTimestamp EarliestMark = new(1, 1);
    private static readonly BsonTimestamp MarkAheadOfCluster = new(int.MaxValue, 1);

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadAsync_EventsInSequence_EndsAtLastEvent(CancellationToken ct)
    {
        await using var scenario = await CatchUpScenario.StartAsync(ct);
        await scenario.AppendAsync("existing", 2);
        var reader = scenario.CreateCatchUpReader(Builders<BsonDocument>.Filter.Empty);

        await using var catchUp = await reader.ReadAsync(SubscriptionOrigin.Start, EarliestMark, ct);

        catchUp.EndPosition.ShouldBe(LogPosition.FromInt64(1));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadAsync_EmptySequence_HasNoEnd(CancellationToken ct)
    {
        await using var scenario = await CatchUpScenario.StartAsync(ct);
        await scenario.AppendAsync("existing", 2);
        var noEvents = Builders<BsonDocument>.Filter.Eq(EventLogEntry.FieldNames.StreamId, "no-such-stream");
        var reader = scenario.CreateCatchUpReader(noEvents);

        await using var catchUp = await reader.ReadAsync(SubscriptionOrigin.Start, EarliestMark, ct);

        catchUp.EndPosition.ShouldBeNull();
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadAsync_MarkAheadOfCluster_Throws(CancellationToken ct)
    {
        await using var scenario = await CatchUpScenario.StartAsync(ct);
        var reader = scenario.CreateCatchUpReader(Builders<BsonDocument>.Filter.Empty);

        // The server refuses a time later than its own cluster time, rather than waiting for it.
        var exception = await Should.ThrowAsync<MongoCommandException>(async () =>
            await reader.ReadAsync(SubscriptionOrigin.Start, MarkAheadOfCluster, ct));

        exception.Code.ShouldBe(InvalidOptionsCode);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadAll_AfterEndRead_WaitsForItsOperationTime(CancellationToken ct)
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

        await using var catchUp = await reader.ReadAsync(SubscriptionOrigin.Start, EarliestMark, ct);

        await foreach (var _ in catchUp.ReadAll().WithCancellation(ct))
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