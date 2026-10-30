using DomainBlocks.Testing.Integration.EventStore;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

/// <summary>
/// Tests that each read of the catch-up reader waits for the mark that it is given, and that a read of the end returns
/// a mark that covers what it found.
/// </summary>
[TestFixture]
public class MongoCatchUpReaderTests
{
    private const int InvalidOptionsCode = 72;

    private static readonly BsonTimestamp EarliestMark = new(1, 1);
    private static readonly BsonTimestamp MarkAheadOfCluster = new(int.MaxValue, 1);

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadEndAsync_EventsInSequence_ReturnsEndAndMarkCoveringIt(CancellationToken ct)
    {
        await using var scenario = await CatchUpScenario.StartAsync(ct);
        var deliveredClusterTime = await AppendLiveEventAsync(scenario);
        var reader = scenario.CreateCatchUpReader(Builders<BsonDocument>.Filter.Empty);

        var (end, mark) = await reader.ReadEndAsync(EarliestMark, "sub-1", ct);

        end.ShouldBe(0);
        mark.ShouldBeGreaterThanOrEqualTo(deliveredClusterTime);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadEndAsync_EmptySequence_ReturnsNoEndAndMarkAdvancedByRead(CancellationToken ct)
    {
        await using var scenario = await CatchUpScenario.StartAsync(ct);
        var deliveredClusterTime = await AppendLiveEventAsync(scenario);
        var noEvents = Builders<BsonDocument>.Filter.Eq(EventLogEntry.FieldNames.StreamId, "no-such-stream");
        var reader = scenario.CreateCatchUpReader(noEvents);

        var (end, mark) = await reader.ReadEndAsync(EarliestMark, "sub-1", ct);

        end.ShouldBeNull();
        mark.ShouldBeGreaterThanOrEqualTo(deliveredClusterTime);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadEndAsync_MarkAheadOfCluster_Throws(CancellationToken ct)
    {
        await using var scenario = await CatchUpScenario.StartAsync(ct);
        var reader = scenario.CreateCatchUpReader(Builders<BsonDocument>.Filter.Empty);

        var exception = await Should.ThrowAsync<MongoCommandException>(async () =>
            await reader.ReadEndAsync(MarkAheadOfCluster, "sub-1", ct));

        exception.Code.ShouldBe(InvalidOptionsCode);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadAsync_MarkAheadOfCluster_Throws(CancellationToken ct)
    {
        await using var scenario = await CatchUpScenario.StartAsync(ct);
        var reader = scenario.CreateCatchUpReader(Builders<BsonDocument>.Filter.Empty);

        var exception = await Should.ThrowAsync<MongoCommandException>(async () =>
        {
            await foreach (var _ in reader.ReadAsync(MarkAheadOfCluster, -1, 0, ct))
            {
            }
        });

        exception.Code.ShouldBe(InvalidOptionsCode);
    }

    private static async Task<BsonTimestamp> AppendLiveEventAsync(CatchUpScenario scenario)
    {
        await using var subscription = scenario.SubscribeToAll();

        (await subscription.NextAsync()).IsCaughtUp.ShouldBeTrue();

        await scenario.AppendLiveAsync("live", 1);

        return scenario.ChangeStreamProbe.LastDeliveredClusterTime.ShouldNotBeNull();
    }
}