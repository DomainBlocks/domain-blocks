using DomainBlocks.EventStore.Filtering;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Unit.Subscriptions;

public class CatchUpSubscriptionFactoryTests
{
    private const int TestTimeoutMillis = 5 * 1000;

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task SubscribeToAll_FromStart_ReplaysLogThenDeliversLiveEvents(CancellationToken ct)
    {
        await using var scenario = new SubscriptionScenario(ct);
        var existing = scenario.EventLog.Append("existing", 2);
        await using var subscription = scenario.SubscribeToAll(SubscriptionOrigin.Start);

        var messages = await subscription.ReadUntilCaughtUpAsync();
        var live = scenario.EventLog.Append("live", 2);

        Payloads(messages).ShouldBe(existing);
        (await subscription.NextEventAsync()).ShouldBe(live[0]);
        (await subscription.NextEventAsync()).ShouldBe(live[1]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task SubscribeToAll_FromEnd_DeliversOnlyEventsAppendedAfterStart(CancellationToken ct)
    {
        await using var scenario = new SubscriptionScenario(ct);
        scenario.EventLog.Append("existing", 2);
        await using var subscription = scenario.SubscribeToAll();

        (await subscription.NextAsync()).IsCaughtUp.ShouldBeTrue();
        var live = scenario.EventLog.Append("live", 1);

        (await subscription.NextEventAsync()).ShouldBe(live[0]);
        scenario.EventLog.CatchUpReads.ShouldBeEmpty();
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task SubscribeToAll_FromEndQueueOverflowsBeforeCaughtUp_DeliversEventsAfterEnd(
        CancellationToken ct)
    {
        await using var scenario = new SubscriptionScenario(ct);
        scenario.EventLog.Append("existing", 2);
        await using var subscription = scenario.SubscribeToAll(options: new SubscriptionOptions { QueueCapacity = 1 });
        var hold = scenario.EventLog.HoldCatchUpEndRead(0);

        // The first catch-up reads the end of the log, which is where the subscription starts, and is then held.
        var pending = subscription.NextAsync();
        await hold.Reached.WaitAsync(ct);

        // More events arrive live than the queue holds, so the subscription has to restart before it has caught up.
        var live = await scenario.EventLog.AppendLiveAsync("live", 2, ct);

        hold.Release();
        var messages = await subscription.ReadUntilCaughtUpAsync(await pending);

        // The restart resumes from the end that the first catch-up read, not from a later one.
        scenario.EventLog.CatchUpReads.ShouldBe(
        [
            new CatchUpRead(FromExclusive: 2, EndInclusive: 4, IsCompleted: true)
        ]);

        messages.Select(x => x.Kind).ShouldBe(
        [
            SubscriptionMessageKind.Event,
            SubscriptionMessageKind.Event,
            SubscriptionMessageKind.CaughtUp
        ]);

        Payloads(messages).ShouldBe(live);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task SubscribeToAll_EventAppendedBeforeCatchUpReadsEnd_DeliversEventOnce(CancellationToken ct)
    {
        await using var scenario = new SubscriptionScenario(ct);
        var existing = scenario.EventLog.Append("existing", 1);
        await using var subscription = scenario.SubscribeToAll(SubscriptionOrigin.Start);
        string[] between = [];

        // The queue is already attached, so the event reaches both the queue and the replay.
        scenario.EventLog.OnCatchUpBegin = () => between = scenario.EventLog.Append("between", 1);

        var messages = await subscription.ReadUntilCaughtUpAsync();
        var live = scenario.EventLog.Append("live", 1);

        Payloads(messages).ShouldBe([existing[0], between[0]]);
        (await subscription.NextEventAsync()).ShouldBe(live[0]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task SubscribeToAll_QueueOverflowsDuringReplay_FinishesReplayBeforeRestarting(CancellationToken ct)
    {
        await using var scenario = new SubscriptionScenario(ct);
        var existing = scenario.EventLog.Append("existing", 3);

        await using var subscription = scenario.SubscribeToAll(
            SubscriptionOrigin.Start,
            new SubscriptionOptions { QueueCapacity = 1 });

        var hold = scenario.EventLog.HoldCatchUpRead(0);

        // The replay delivers its first event and is then held, with the rest still to come.
        (await subscription.NextEventAsync()).ShouldBe(existing[0]);
        var pending = subscription.NextAsync();

        // More events arrive live than the queue holds, so the subscription has to restart.
        var live = await scenario.EventLog.AppendLiveAsync("live", 2, ct);

        hold.Release();
        var messages = await subscription.ReadUntilCaughtUpAsync(await pending);

        // The first replay ran to its end position, and the second continued from there.
        scenario.EventLog.CatchUpReads.ShouldBe(
        [
            new CatchUpRead(FromExclusive: null, EndInclusive: 3, IsCompleted: true),
            new CatchUpRead(FromExclusive: 3, EndInclusive: 5, IsCompleted: true)
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

        Payloads(messages).ShouldBe([existing[1], existing[2], live[0], live[1]]);

        // The restart attached its new queue before it detached the old one, so the log was read without a break.
        scenario.EventLog.ListenerStartCount.ShouldBe(1);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task SubscribeToAll_QueueOverflowsAgainBeforeCatchingUpAgain_ReportsFellBehindOnce(
        CancellationToken ct)
    {
        await using var scenario = new SubscriptionScenario(ct);
        var existing = scenario.EventLog.Append("existing", 1);

        await using var subscription = scenario.SubscribeToAll(
            SubscriptionOrigin.Start,
            new SubscriptionOptions { QueueCapacity = 1 });

        var hold = scenario.EventLog.HoldCatchUpRead(1);

        (await subscription.NextEventAsync()).ShouldBe(existing[0]);
        (await subscription.NextAsync()).IsCaughtUp.ShouldBeTrue();

        // The queue overflows while the subscriber is caught up, so it is told that it fell behind.
        var first = await scenario.EventLog.AppendLiveAsync("first", 3, ct);

        (await subscription.NextEventAsync()).ShouldBe(first[0]);
        (await subscription.NextAsync()).IsFellBehind.ShouldBeTrue();

        // The replay that follows delivers its first event and is then held, with the rest still to come.
        (await subscription.NextEventAsync()).ShouldBe(first[1]);
        var pending = subscription.NextAsync();

        // The queue overflows again before the subscriber has caught up again.
        var second = await scenario.EventLog.AppendLiveAsync("second", 2, ct);

        hold.Release();
        var messages = await subscription.ReadUntilCaughtUpAsync(await pending);

        // It took a third replay to catch up, and the subscriber was not told a second time that it fell behind.
        scenario.EventLog.CatchUpReads.Count.ShouldBe(3);

        messages.Select(x => x.Kind).ShouldBe(
        [
            SubscriptionMessageKind.Event,
            SubscriptionMessageKind.Event,
            SubscriptionMessageKind.Event,
            SubscriptionMessageKind.CaughtUp
        ]);

        Payloads(messages).ShouldBe([first[2], second[0], second[1]]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task SubscribeToAll_RestartAfterFilteredReplay_ResumesFromReplayEnd(CancellationToken ct)
    {
        await using var scenario = new SubscriptionScenario(ct);

        // The filter selects the first of the existing events and not the two that follow it.
        var selected = scenario.EventLog.Append("existing", 1, "selected");
        scenario.EventLog.Append("existing", 2, "excluded");

        await using var subscription = scenario.SubscribeToAll(
            SubscriptionOrigin.Start,
            new SubscriptionOptions { QueueCapacity = 1, Filter = EventFilter.StreamIds("selected") });

        var hold = scenario.EventLog.HoldCatchUpRead(0);

        // The replay delivers the event that the filter selects and is then held.
        (await subscription.NextEventAsync()).ShouldBe(selected[0]);
        var pending = subscription.NextAsync();

        // More events that the filter selects arrive live than the queue holds, so the subscription has to restart.
        var live = await scenario.EventLog.AppendLiveAsync("live", 2, ct, "selected");

        hold.Release();
        var messages = await subscription.ReadUntilCaughtUpAsync(await pending);

        // The first replay delivered nothing after its first event, but it covered the log up to its end position, so
        // the second continues from there rather than from the last event that was delivered.
        scenario.EventLog.CatchUpReads.ShouldBe(
        [
            new CatchUpRead(FromExclusive: null, EndInclusive: 3, IsCompleted: true),
            new CatchUpRead(FromExclusive: 3, EndInclusive: 5, IsCompleted: true)
        ]);

        Payloads(messages).ShouldBe(live);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task SubscribeToAll_TailAlreadyRunning_ReplaysDeliveredEventsAndSharesTail(CancellationToken ct)
    {
        await using var scenario = new SubscriptionScenario(ct);
        await using var first = scenario.SubscribeToAll();
        (await first.NextAsync()).IsCaughtUp.ShouldBeTrue();
        var delivered = await scenario.EventLog.AppendLiveAsync("delivered", 2, ct);

        await using var second = scenario.SubscribeToAll(SubscriptionOrigin.Start);
        var messages = await second.ReadUntilCaughtUpAsync();
        var live = scenario.EventLog.Append("live", 1);

        Payloads(messages).ShouldBe(delivered);
        (await second.NextEventAsync()).ShouldBe(live[0]);

        (await first.NextEventAsync()).ShouldBe(delivered[0]);
        (await first.NextEventAsync()).ShouldBe(delivered[1]);
        (await first.NextEventAsync()).ShouldBe(live[0]);

        scenario.EventLog.ListenerStartCount.ShouldBe(1);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task SubscribeToStream_FromStart_DeliversOnlyStreamEvents(CancellationToken ct)
    {
        await using var scenario = new SubscriptionScenario(ct);
        scenario.EventLog.Append("other", 1, "s1");
        var existing = scenario.EventLog.Append("existing", 2, "s2");
        await using var subscription = scenario.SubscribeToStream("s2", SubscriptionOrigin.Start);

        var messages = await subscription.ReadUntilCaughtUpAsync();

        // Some of these have stream positions past the end of the replay, so only the stream ID tells them apart.
        scenario.EventLog.Append("other", 2, "s1");
        var live = scenario.EventLog.Append("live", 1, "s2");

        Payloads(messages).ShouldBe(existing);
        (await subscription.NextEventAsync()).ShouldBe(live[0]);

        // The replay follows positions in the stream, not in the log.
        scenario.EventLog.CatchUpReads.ShouldBe(
        [
            new CatchUpRead(FromExclusive: null, EndInclusive: 1, IsCompleted: true)
        ]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task SubscribeToStream_AfterPosition_ReplaysEventsAfterSpecifiedStreamPosition(CancellationToken ct)
    {
        await using var scenario = new SubscriptionScenario(ct);
        var existing = scenario.EventLog.Append("existing", 3, "s2");
        scenario.EventLog.Append("other", 1, "s1");

        await using var subscription = scenario.SubscribeToStream(
            "s2",
            SubscriptionOrigin.After(StreamPosition.FromInt64(0)));

        var messages = await subscription.ReadUntilCaughtUpAsync();

        // The replay starts after the stream position, without reading the stream's earlier events.
        Payloads(messages).ShouldBe([existing[1], existing[2]]);
        scenario.EventLog.CatchUpReads.ShouldBe(
        [
            new CatchUpRead(FromExclusive: 0, EndInclusive: 2, IsCompleted: true)
        ]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task SubscribeToAll_AfterPosition_ReplaysEventsAfterSpecifiedPosition(CancellationToken ct)
    {
        await using var scenario = new SubscriptionScenario(ct);
        var existing = scenario.EventLog.Append("existing", 3);

        await using var subscription = scenario.SubscribeToAll(SubscriptionOrigin.After(LogPosition.FromInt64(1)));
        var messages = await subscription.ReadUntilCaughtUpAsync();

        Payloads(messages).ShouldBe([existing[1], existing[2]]);
        scenario.EventLog.CatchUpReads.ShouldBe(
        [
            new CatchUpRead(FromExclusive: 1, EndInclusive: 3, IsCompleted: true)
        ]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task SubscribeToAll_Filtered_DeliversMatchingLiveEvents(CancellationToken ct)
    {
        await using var scenario = new SubscriptionScenario(ct);

        await using var subscription = scenario.SubscribeToAll(
            options: new SubscriptionOptions { Filter = EventFilter.StreamIds("selected") });

        (await subscription.NextAsync()).IsCaughtUp.ShouldBeTrue();
        scenario.EventLog.Append("excluded", 1, "excluded");
        var selected = scenario.EventLog.Append("selected", 1, "selected");

        (await subscription.NextEventAsync()).ShouldBe(selected[0]);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task SubscribeToAll_TailFails_ThrowsTailError(CancellationToken ct)
    {
        await using var scenario = new SubscriptionScenario(ct);
        await using var subscription = scenario.SubscribeToAll();
        (await subscription.NextAsync()).IsCaughtUp.ShouldBeTrue();
        var error = new InvalidOperationException("misconfigured");
        scenario.EventLog.FailNextReads(error);

        scenario.EventLog.Append("live", 1);

        var thrown = await Should.ThrowAsync<InvalidOperationException>(subscription.NextAsync);
        thrown.ShouldBeSameAs(error);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task SubscribeToAll_CatchUpFails_ThrowsCatchUpError(CancellationToken ct)
    {
        await using var scenario = new SubscriptionScenario(ct);
        await using var subscription = scenario.SubscribeToAll(SubscriptionOrigin.Start);
        var error = new InvalidOperationException("unavailable");
        scenario.EventLog.FailNextCatchUpEndRead(error);

        var thrown = await Should.ThrowAsync<InvalidOperationException>(subscription.NextAsync);

        thrown.ShouldBeSameAs(error);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task SubscribeToAll_LastSubscriptionDisposed_StopsReadingLog(CancellationToken ct)
    {
        await using var scenario = new SubscriptionScenario(ct);
        var subscription = scenario.SubscribeToAll();
        (await subscription.NextAsync()).IsCaughtUp.ShouldBeTrue();

        await subscription.DisposeAsync();

        scenario.EventLog.ActiveListenerCount.ShouldBe(0);
    }

    private static string[] Payloads(
        IEnumerable<SubscriptionMessage<string, string, StreamPosition, LogPosition>> messages)
    {
        return messages.Where(x => x.Event is not null).Select(x => x.Event!.Value.Payload).ToArray();
    }
}