using System.Net;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.Testing.Integration.EventStore;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Connections;
using MongoDB.Driver.Core.Servers;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

/// <summary>
/// How a subscription replays the log when it has to restart.
/// </summary>
[TestFixture]
public class MongoSubscriptionCatchUpTests
{
    private const int UnauthorizedCode = 13;

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_WhenQueueOverflowsDuringReplay_FinishesReplayBeforeRestarting(CancellationToken ct)
    {
        await using var scenario = await CatchUpScenario.StartAsync(ct);
        var existing = await scenario.AppendAsync("existing", 3);
        var hold = scenario.HoldCatchUp(0);

        await using var subscription = scenario.SubscribeToAll(new SubscriptionOptions { QueueCapacity = 1 });

        // The replay delivers its first event and is then held, with the rest still to come.
        (await subscription.NextEventAsync()).ShouldBe(existing[0]);
        var pending = subscription.NextAsync();

        // More events arrive live than the queue holds, so the subscription has to restart.
        var live = await scenario.AppendLiveAsync("live", 2);

        hold.Release();
        var messages = await subscription.ReadUntilCaughtUpAsync(await pending);

        // The first replay ran to its end position, and the second continued from there.
        scenario.CatchUpReads.ShouldBe(
        [
            new CatchUpRead(StartExclusive: -1, EndInclusive: 2, IsCompleted: true),
            new CatchUpRead(StartExclusive: 2, EndInclusive: 4, IsCompleted: true)
        ]);

        // The subscriber had not been told that it caught up, so it is not told that it fell behind.
        messages.Select(x => x.Kind).ShouldBe(
        [
            SubscriptionMessageKind.Event,
            SubscriptionMessageKind.Event,
            SubscriptionMessageKind.Event,
            SubscriptionMessageKind.Event,
            SubscriptionMessageKind.CaughtUp
        ]);

        messages
            .Where(x => x.Event is not null)
            .Select(x => x.Event!.Value.Payload)
            .ShouldBe([existing[1], existing[2], live[0], live[1]]);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_WhenQueueOverflowsAgainBeforeCatchingUpAgain_ReportsFellBehindOnce(
        CancellationToken ct)
    {
        await using var scenario = await CatchUpScenario.StartAsync(ct);
        var existing = await scenario.AppendAsync("existing", 1);
        var hold = scenario.HoldCatchUp(1);

        await using var subscription = scenario.SubscribeToAll(new SubscriptionOptions { QueueCapacity = 1 });

        (await subscription.NextEventAsync()).ShouldBe(existing[0]);
        (await subscription.NextAsync()).IsCaughtUp.ShouldBeTrue();

        // The queue overflows while the subscriber is caught up, so it is told that it fell behind.
        var first = await scenario.AppendLiveAsync("first", 3);

        (await subscription.NextEventAsync()).ShouldBe(first[0]);
        (await subscription.NextAsync()).IsFellBehind.ShouldBeTrue();

        // The replay that follows delivers its first event and is then held, with the rest still to come.
        (await subscription.NextEventAsync()).ShouldBe(first[1]);
        var pending = subscription.NextAsync();

        // The queue overflows again before the subscriber has caught up again.
        var second = await scenario.AppendLiveAsync("second", 2);

        hold.Release();
        var messages = await subscription.ReadUntilCaughtUpAsync(await pending);

        // It took a third replay to catch up, and the subscriber was not told a second time that it fell behind.
        scenario.CatchUpReads.Count.ShouldBe(3);

        messages.Select(x => x.Kind).ShouldBe(
        [
            SubscriptionMessageKind.Event,
            SubscriptionMessageKind.Event,
            SubscriptionMessageKind.Event,
            SubscriptionMessageKind.CaughtUp
        ]);

        messages
            .Where(x => x.Event is not null)
            .Select(x => x.Event!.Value.Payload)
            .ShouldBe([first[2], second[0], second[1]]);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_WhenRestartingAfterFilteredCatchUp_ResumesFromLastObservedPosition(
        CancellationToken ct)
    {
        await using var scenario = await CatchUpScenario.StartAsync(ct);

        // The filter selects the first of the existing events and not the two that follow it.
        var selected = await scenario.AppendAsync("existing", 1, "selected");
        await scenario.AppendAsync("existing", 2, "excluded");
        var hold = scenario.HoldCatchUp(0);

        await using var subscription = scenario.SubscribeToAll(
            new SubscriptionOptions { QueueCapacity = 1 },
            Builders<BsonDocument>.Filter.Eq(EventLogEntry.FieldNames.StreamId, "selected"),
            EventFilter.StreamIds("selected"));

        // The replay delivers the event that the filter selects and is then held.
        (await subscription.NextEventAsync()).ShouldBe(selected[0]);
        var pending = subscription.NextAsync();

        // More events that the filter selects arrive live than the queue holds, so the subscription has to restart.
        var live = await scenario.AppendLiveAsync("live", 2, "selected");

        hold.Release();
        var messages = await subscription.ReadUntilCaughtUpAsync(await pending);

        // The first replay delivered nothing after its first event, but it covered the log up to its end position, so
        // the second should continue from there rather than from the last event that was delivered.
        scenario.CatchUpReads.ShouldBe(
        [
            new CatchUpRead(StartExclusive: -1, EndInclusive: 2, IsCompleted: true),
            new CatchUpRead(StartExclusive: 2, EndInclusive: 4, IsCompleted: true)
        ]);

        messages
            .Where(x => x.Event is not null)
            .Select(x => x.Event!.Value.Payload)
            .ShouldBe([live[0], live[1]]);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_WhenReplayCursorIsKilled_ResumesAfterLastEvent(CancellationToken ct)
    {
        await using var scenario = await CatchUpScenario.StartAsync(ct);

        // A find's first batch holds fewer events than this, so the replay reads the rest with getMore.
        var existing = await scenario.AppendAsync("existing", 150);
        var hold = scenario.HoldCatchUp(0);

        await using var subscription = scenario.SubscribeToAll();

        (await subscription.NextEventAsync()).ShouldBe(existing[0]);
        var pending = subscription.NextAsync();

        await scenario.KillReplayCursorsAsync();

        hold.Release();
        var messages = await subscription.ReadUntilCaughtUpAsync(await pending);

        // The first replay failed at its getMore, and the second continued after the last event delivered.
        scenario.CatchUpReads.Count.ShouldBe(2);
        scenario.CatchUpReads[0].IsCompleted.ShouldBeFalse();

        var resumedRead = scenario.CatchUpReads[1];
        resumedRead.StartExclusive.ShouldBeGreaterThan(0);
        resumedRead.EndInclusive.ShouldBe(149);
        resumedRead.IsCompleted.ShouldBeTrue();

        messages
            .Where(x => x.Event is not null)
            .Select(x => x.Event.ShouldNotBeNull().Payload)
            .ShouldBe(existing.Skip(1));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_WhenReplayCursorIsKilledWithoutRetries_Throws(CancellationToken ct)
    {
        await using var scenario = await CatchUpScenario.StartAsync(ct);

        // A find's first batch holds fewer events than this, so the replay reads the rest with getMore.
        var existing = await scenario.AppendAsync("existing", 150);
        var hold = scenario.HoldCatchUp(0);

        await using var subscription = scenario.SubscribeToAll(new SubscriptionOptions { MaxRetryAttempts = 0 });

        (await subscription.NextEventAsync()).ShouldBe(existing[0]);
        var pending = subscription.NextAsync();

        await scenario.KillReplayCursorsAsync();

        hold.Release();

        await Should.ThrowAsync<MongoCursorNotFoundException>(async () =>
            await subscription.ReadUntilCaughtUpAsync(await pending));

        scenario.CatchUpReads.Count.ShouldBe(1);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_WhenChangeHistoryIsLost_RestartsAndDeliversEventsAppendedMeanwhile(
        CancellationToken ct)
    {
        await using var scenario = await CatchUpScenario.StartAsync(ct);
        var existing = await scenario.AppendAsync("existing", 1);

        await using var subscription = scenario.SubscribeToAll();

        (await subscription.NextEventAsync()).ShouldBe(existing[0]);
        (await subscription.NextAsync()).IsCaughtUp.ShouldBeTrue();

        // The change stream loses its connection, and events are appended before it tries to resume.
        var disconnect = scenario.ChangeStreamProbe.SimulateDisconnect();

        await disconnect.ResumptionAttempt.WaitAsync(ct);

        var meanwhile = await scenario.AppendAsync("meanwhile", 2);

        // The oplog no longer holds the resume point, so the subscription restarts with a new change stream.
        disconnect.FailResumption(CreateCommandException(MongoErrorCodes.ChangeStreamHistoryLost));
        var messages = await subscription.ReadUntilCaughtUpAsync();

        messages.Select(x => x.Kind).ShouldBe(
        [
            SubscriptionMessageKind.FellBehind,
            SubscriptionMessageKind.Event,
            SubscriptionMessageKind.Event,
            SubscriptionMessageKind.CaughtUp
        ]);

        messages
            .Where(x => x.Event is not null)
            .Select(x => x.Event!.Value.Payload)
            .ShouldBe([meanwhile[0], meanwhile[1]]);

        scenario.ChangeStreamProbe.InitialOpenCount.ShouldBe(2);

        // The new change stream delivers live events.
        var live = await scenario.AppendAsync("live", 1);
        (await subscription.NextEventAsync()).ShouldBe(live[0]);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_WhenChangeStreamFailsOtherwise_Throws(CancellationToken ct)
    {
        await using var scenario = await CatchUpScenario.StartAsync(ct);
        await using var subscription = scenario.SubscribeToAll();

        (await subscription.NextAsync()).IsCaughtUp.ShouldBeTrue();

        var error = CreateCommandException(UnauthorizedCode);
        scenario.ChangeStreamProbe.SimulateDisconnect().FailResumption(error);

        var exception = await Should.ThrowAsync<MongoCommandException>(subscription.NextAsync);

        exception.ShouldBeSameAs(error);
        scenario.ChangeStreamProbe.InitialOpenCount.ShouldBe(1);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_AfterLiveDelivery_WaitsForDeliveredClusterTime(CancellationToken ct)
    {
        await using var scenario = await CatchUpScenario.StartAsync(ct);
        await using var subscription1 = scenario.SubscribeToAll();

        (await subscription1.NextAsync()).IsCaughtUp.ShouldBeTrue();

        var liveEvents = await scenario.AppendLiveAsync("live", 1);

        // The second subscription joins the stream after it delivered the event, so only its catch-up can deliver it.
        // After a failover, the new primary's commit point can lag behind the event, and a catch-up that waited only
        // for the stream's anchor would not see it.
        await using var subscription2 = scenario.SubscribeToAll();

        (await subscription2.NextEventAsync()).ShouldBe(liveEvents[0]);
        var deliveredClusterTime = scenario.ChangeStreamProbe.LastDeliveredClusterTime.ShouldNotBeNull();
        scenario.CatchUpOpTimes[^1].ShouldBeGreaterThanOrEqualTo(deliveredClusterTime);
    }

    private static MongoCommandException CreateCommandException(int code)
    {
        var serverId = new ServerId(new ClusterId(), new IPEndPoint(IPAddress.Loopback, 27017));

        return new MongoCommandException(
            new ConnectionId(serverId),
            "The command failed.",
            new BsonDocument("aggregate", 1),
            new BsonDocument { { "ok", 0 }, { "code", code } });
    }
}