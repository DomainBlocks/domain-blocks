using DomainBlocks.EventStore;
using DomainBlocks.EventStore.Abstractions;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.Testing.Integration;

public abstract class EventStoreSubscriptionTests<TStreamPos, TLogPos> :
    EventStoreTestBase<object, string, TStreamPos, TLogPos>
    where TStreamPos : notnull
    where TLogPos : notnull
{
    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_FromStart_ReadsCatchUpThenLiveEvents(CancellationToken cancellationToken)
    {
        var streamId = NewStreamId();
        var catchUpEvents = CreateEvents("catch-up-1", "catch-up-2");
        await EventStore.AppendAsync(streamId, catchUpEvents, cancellationToken: cancellationToken);

        await using var enumerator = EventStore
            .SubscribeToAll(SubscriptionOrigin.Start<TLogPos>())
            .GetAsyncEnumerator(cancellationToken);

        (await GetNextEventAsync(enumerator)).Payload.ShouldBe(catchUpEvents[0]);
        (await GetNextEventAsync(enumerator)).Payload.ShouldBe(catchUpEvents[1]);
        await ShouldBeCaughtUpAsync(enumerator);

        var liveEvent = new TestEvent { Value = "live" };
        await EventStore.AppendAsync(streamId, [liveEvent], cancellationToken: cancellationToken);

        (await GetNextEventAsync(enumerator)).Payload.ShouldBe(liveEvent);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_WithDefaultOrigin_IgnoresExistingAndReadsLiveEvents(
        CancellationToken cancellationToken)
    {
        var streamId = NewStreamId();
        await EventStore.AppendAsync(streamId, CreateEvents("existing"), cancellationToken: cancellationToken);

        await using var enumerator = EventStore.SubscribeToAll().GetAsyncEnumerator(cancellationToken);

        await ShouldBeCaughtUpAsync(enumerator);

        var liveEvent = new TestEvent { Value = "live" };
        await EventStore.AppendAsync(streamId, [liveEvent], cancellationToken: cancellationToken);

        (await GetNextEventAsync(enumerator)).Payload.ShouldBe(liveEvent);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_FromStartWhenStoreIsEmpty_EmitsCaughtUpThenReadsLiveEvent(
        CancellationToken cancellationToken)
    {
        await using var enumerator = EventStore
            .SubscribeToAll(SubscriptionOrigin.Start<TLogPos>())
            .GetAsyncEnumerator(cancellationToken);

        await ShouldBeCaughtUpAsync(enumerator);

        var liveEvent = new TestEvent { Value = "first" };
        await EventStore.AppendAsync(NewStreamId(), [liveEvent], cancellationToken: cancellationToken);

        (await GetNextEventAsync(enumerator)).Payload.ShouldBe(liveEvent);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_AfterPosition_ExcludesBoundaryAndReadsFollowingEvents(
        CancellationToken cancellationToken)
    {
        var streamId = NewStreamId();
        var events = CreateEvents("event-0", "event-1", "event-2");
        await EventStore.AppendAsync(streamId, events, cancellationToken: cancellationToken);

        var storedEvents = await EventStore.ReadAll().ToArrayAsync(cancellationToken);
        var boundary = storedEvents[0].Context.LogPosition;

        await using var enumerator = EventStore
            .SubscribeToAll(SubscriptionOrigin.After(boundary))
            .GetAsyncEnumerator(cancellationToken);

        (await GetNextEventAsync(enumerator)).Payload.ShouldBe(events[1]);
        (await GetNextEventAsync(enumerator)).Payload.ShouldBe(events[2]);
        await ShouldBeCaughtUpAsync(enumerator);

        var liveEvent = new TestEvent { Value = "live" };
        await EventStore.AppendAsync(streamId, [liveEvent], cancellationToken: cancellationToken);
        (await GetNextEventAsync(enumerator)).Payload.ShouldBe(liveEvent);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_AfterFinalPosition_EmitsCaughtUpThenReadsLiveEvent(
        CancellationToken cancellationToken)
    {
        var streamId = NewStreamId();
        await EventStore.AppendAsync(streamId, CreateEvents("existing"), cancellationToken: cancellationToken);

        var finalPosition = (await EventStore.ReadAll().SingleAsync(cancellationToken)).Context.LogPosition;

        await using var enumerator = EventStore
            .SubscribeToAll(SubscriptionOrigin.After(finalPosition))
            .GetAsyncEnumerator(cancellationToken);

        await ShouldBeCaughtUpAsync(enumerator);

        var liveEvent = new TestEvent { Value = "live" };
        await EventStore.AppendAsync(streamId, [liveEvent], cancellationToken: cancellationToken);
        (await GetNextEventAsync(enumerator)).Payload.ShouldBe(liveEvent);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_WithMultipleStreams_ObservesAllEvents(CancellationToken cancellationToken)
    {
        var stream1 = NewStreamId();
        var stream2 = NewStreamId();
        var events = CreateEvents("stream-1-event", "stream-2-event", "stream-1-event-2");

        await EventStore.AppendAsync(stream1, [events[0]], cancellationToken: cancellationToken);
        await EventStore.AppendAsync(stream2, [events[1]], cancellationToken: cancellationToken);
        await EventStore.AppendAsync(stream1, [events[2]], cancellationToken: cancellationToken);

        await using var enumerator = EventStore
            .SubscribeToAll(SubscriptionOrigin.Start<TLogPos>())
            .GetAsyncEnumerator(cancellationToken);

        var observed = new[]
        {
            await GetNextEventAsync(enumerator),
            await GetNextEventAsync(enumerator),
            await GetNextEventAsync(enumerator)
        };

        observed.Select(x => x.Payload).ShouldBe(events);
        observed.Select(x => x.Context.StreamId).ShouldBe([stream1, stream2, stream1]);
        observed.Select(x => x.Context.LogPosition).Distinct().Count().ShouldBe(3);
        await ShouldBeCaughtUpAsync(enumerator);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToStream_FromStart_ObservesRequestedStreamOnly(CancellationToken cancellationToken)
    {
        var targetStream = NewStreamId();
        var otherStream = NewStreamId();
        var targetEvents = CreateEvents("target-1", "target-2");

        await EventStore.AppendAsync(targetStream, [targetEvents[0]], cancellationToken: cancellationToken);
        await EventStore.AppendAsync(otherStream, CreateEvents("other-1"), cancellationToken: cancellationToken);
        await EventStore.AppendAsync(targetStream, [targetEvents[1]], cancellationToken: cancellationToken);

        await using var enumerator = EventStore
            .SubscribeToStream(targetStream, SubscriptionOrigin.Start<TStreamPos>())
            .GetAsyncEnumerator(cancellationToken);

        (await GetNextEventAsync(enumerator)).Payload.ShouldBe(targetEvents[0]);
        (await GetNextEventAsync(enumerator)).Payload.ShouldBe(targetEvents[1]);
        await ShouldBeCaughtUpAsync(enumerator);

        await EventStore.AppendAsync(otherStream, CreateEvents("other-live"), cancellationToken: cancellationToken);
        var targetLiveEvent = new TestEvent { Value = "target-live" };
        await EventStore.AppendAsync(targetStream, [targetLiveEvent], cancellationToken: cancellationToken);

        (await GetNextEventAsync(enumerator)).Payload.ShouldBe(targetLiveEvent);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToStream_WithDefaultOrigin_IgnoresExistingAndReadsTargetLiveEvents(
        CancellationToken cancellationToken)
    {
        var targetStream = NewStreamId();
        var otherStream = NewStreamId();

        await EventStore.AppendAsync(
            targetStream,
            CreateEvents("existing-target"),
            cancellationToken: cancellationToken);

        await EventStore.AppendAsync(otherStream, CreateEvents("existing-other"), cancellationToken: cancellationToken);

        await using var enumerator = EventStore.SubscribeToStream(targetStream).GetAsyncEnumerator(cancellationToken);
        await ShouldBeCaughtUpAsync(enumerator);

        await EventStore.AppendAsync(otherStream, CreateEvents("other-live"), cancellationToken: cancellationToken);
        var targetLiveEvent = new TestEvent { Value = "target-live" };
        await EventStore.AppendAsync(targetStream, [targetLiveEvent], cancellationToken: cancellationToken);

        (await GetNextEventAsync(enumerator)).Payload.ShouldBe(targetLiveEvent);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToStream_AfterPosition_ExcludesBoundaryAndOtherStreams(
        CancellationToken cancellationToken)
    {
        var targetStream = NewStreamId();
        var otherStream = NewStreamId();
        var targetEvents = CreateEvents("target-0", "target-1", "target-2");

        await EventStore.AppendAsync(targetStream, targetEvents, cancellationToken: cancellationToken);

        await EventStore.AppendAsync(
            otherStream,
            CreateEvents("other-0", "other-1"),
            cancellationToken: cancellationToken);

        var storedTargetEvents = await EventStore.ReadStream(targetStream).ToArrayAsync(cancellationToken);
        var boundary = storedTargetEvents[0].Context.StreamPosition;

        await using var enumerator = EventStore
            .SubscribeToStream(targetStream, SubscriptionOrigin.After(boundary))
            .GetAsyncEnumerator(cancellationToken);

        var first = await GetNextEventAsync(enumerator);
        var second = await GetNextEventAsync(enumerator);
        first.Payload.ShouldBe(targetEvents[1]);
        first.Context.StreamId.ShouldBe(targetStream);
        second.Payload.ShouldBe(targetEvents[2]);
        second.Context.StreamId.ShouldBe(targetStream);
        await ShouldBeCaughtUpAsync(enumerator);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_WithAppendedEvent_PreservesEventContextAndMetadata(
        CancellationToken cancellationToken)
    {
        var streamId = NewStreamId();
        var expected = new TestEvent { Value = "with-metadata" };

        var appendable = AppendableEvent.Create<object>(
            expected,
            [new KeyValuePair<string, string>("tenant", "test-tenant")]);

        await EventStore.AppendAsync(streamId, [appendable], cancellationToken: cancellationToken);
        var stored = await EventStore.ReadStream(streamId).SingleAsync(cancellationToken);

        await using var enumerator = EventStore
            .SubscribeToAll(SubscriptionOrigin.Start<TLogPos>())
            .GetAsyncEnumerator(cancellationToken);

        var observed = await GetNextEventAsync(enumerator);
        observed.Payload.ShouldBe(expected);
        observed.Context.StreamId.ShouldBe(streamId);
        observed.Context.StreamPosition.ShouldBe(stored.Context.StreamPosition);
        observed.Context.LogPosition.ShouldBe(stored.Context.LogPosition);
        observed.Context.Metadata["tenant"].ShouldBe("test-tenant");
        await ShouldBeCaughtUpAsync(enumerator);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToStream_WhenStreamDoesNotExist_EmitsCaughtUpThenObservesFirstEvent(
        CancellationToken cancellationToken)
    {
        var streamId = NewStreamId();

        await using var enumerator = EventStore
            .SubscribeToStream(streamId, SubscriptionOrigin.Start<TStreamPos>())
            .GetAsyncEnumerator(cancellationToken);

        await ShouldBeCaughtUpAsync(enumerator);

        var firstEvent = new TestEvent { Value = "first" };
        await EventStore.AppendAsync(streamId, [firstEvent], cancellationToken: cancellationToken);

        (await GetNextEventAsync(enumerator)).Payload.ShouldBe(firstEvent);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_WithEventsAppendedDuringCatchUp_ObservesEachEvent(
        CancellationToken cancellationToken)
    {
        var streamId = NewStreamId();

        var catchUpEvents = Enumerable.Range(0, 100)
            .Select(x => new TestEvent { Value = $"catch-up-{x}" })
            .ToArray();

        await EventStore.AppendAsync(streamId, catchUpEvents, cancellationToken: cancellationToken);

        await using var enumerator = EventStore
            .SubscribeToAll(SubscriptionOrigin.Start<TLogPos>())
            .GetAsyncEnumerator(cancellationToken);

        List<TestEvent> observed =
        [
            (await GetNextEventAsync(enumerator)).Payload.ShouldBeOfType<TestEvent>()
        ];

        var liveEvent = new TestEvent { Value = "appended-during-catch-up" };

        await EventStore.AppendAsync(streamId, [liveEvent], cancellationToken: cancellationToken);

        for (var i = 1; i < catchUpEvents.Length; i++)
            observed.Add((await GetNextEventAsync(enumerator)).Payload.ShouldBeOfType<TestEvent>());

        await ShouldBeCaughtUpAsync(enumerator);
        observed.Add((await GetNextEventAsync(enumerator)).Payload.ShouldBeOfType<TestEvent>());
        observed.ShouldBe(catchUpEvents.Append(liveEvent));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_WhenQueueOverflows_ReportsFellBehindAndRecovers(
        CancellationToken cancellationToken)
    {
        await using var enumerator = EventStore
            .SubscribeToAll(
                SubscriptionOrigin.Start<TLogPos>(),
                new SubscriptionOptions { QueueCapacity = 1 })
            .GetAsyncEnumerator(cancellationToken);

        await ShouldBeCaughtUpAsync(enumerator);

        var expected = Enumerable.Range(0, 100)
            .Select(x => new TestEvent { Value = $"base-overflow-{x}" })
            .ToArray();

        await EventStore.AppendAsync(NewStreamId(), expected, cancellationToken: cancellationToken);

        var observed = await ReadUntilRecoveredAsync(enumerator);
        observed.Events.ShouldBe(expected);
        observed.FellBehindCount.ShouldBeGreaterThan(0);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_CancellationBeforeEnumeration_CancelsPromptly(CancellationToken cancellationToken)
    {
        using var subscriptionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await subscriptionCancellation.CancelAsync();

        await using var enumerator = EventStore
            .SubscribeToAll(SubscriptionOrigin.Start<TLogPos>())
            .GetAsyncEnumerator(subscriptionCancellation.Token);

        await enumerator.MoveNextAsync().AsTask().ShouldThrowAsync<OperationCanceledException>();
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_CancellationWhileWaitingForLiveEvent_CancelsPromptly(
        CancellationToken cancellationToken)
    {
        using var subscriptionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        await using var enumerator = EventStore
            .SubscribeToAll(SubscriptionOrigin.Start<TLogPos>())
            .GetAsyncEnumerator(subscriptionCancellation.Token);

        await ShouldBeCaughtUpAsync(enumerator);
        await subscriptionCancellation.CancelAsync();

        await enumerator.MoveNextAsync().AsTask().ShouldThrowAsync<OperationCanceledException>();
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_TwoSubscribers_ObserveTheSameLiveEvents(CancellationToken cancellationToken)
    {
        await using var first = EventStore
            .SubscribeToAll(SubscriptionOrigin.Start<TLogPos>())
            .GetAsyncEnumerator(cancellationToken);

        await using var second = EventStore
            .SubscribeToAll(SubscriptionOrigin.Start<TLogPos>())
            .GetAsyncEnumerator(cancellationToken);

        await ShouldBeCaughtUpAsync(first);
        await ShouldBeCaughtUpAsync(second);

        var liveEvent = new TestEvent { Value = "live" };
        await EventStore.AppendAsync(NewStreamId(), [liveEvent], cancellationToken: cancellationToken);

        (await GetNextEventAsync(first)).Payload.ShouldBe(liveEvent);
        (await GetNextEventAsync(second)).Payload.ShouldBe(liveEvent);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_TwoSubscribers_UnsubscribeIndependently(CancellationToken cancellationToken)
    {
        var first = EventStore
            .SubscribeToAll(SubscriptionOrigin.Start<TLogPos>())
            .GetAsyncEnumerator(cancellationToken);

        await using var second = EventStore
            .SubscribeToAll(SubscriptionOrigin.Start<TLogPos>())
            .GetAsyncEnumerator(cancellationToken);

        await ShouldBeCaughtUpAsync(first);
        await ShouldBeCaughtUpAsync(second);

        var firstLive = new TestEvent { Value = "first-live" };
        await EventStore.AppendAsync(NewStreamId(), [firstLive], cancellationToken: cancellationToken);
        (await GetNextEventAsync(first)).Payload.ShouldBe(firstLive);
        (await GetNextEventAsync(second)).Payload.ShouldBe(firstLive);

        await first.DisposeAsync();

        var secondLive = new TestEvent { Value = "second-live" };
        await EventStore.AppendAsync(NewStreamId(), [secondLive], cancellationToken: cancellationToken);
        (await GetNextEventAsync(second)).Payload.ShouldBe(secondLive);
    }

    private static string NewStreamId() => $"test-{Guid.NewGuid():N}";

    private static TestEvent[] CreateEvents(params string[] values) =>
        [.. values.Select(value => new TestEvent { Value = value })];

    private static async Task<ReadEvent<object, string, TStreamPos, TLogPos>> GetNextEventAsync(
        IAsyncEnumerator<SubscriptionMessage> enumerator)
    {
        (await enumerator.MoveNextAsync()).ShouldBeTrue();

        return enumerator.Current
            .ShouldBeOfType<SubscriptionMessage.Event<ReadEvent<object, string, TStreamPos, TLogPos>>>()
            .Value;
    }

    private static async Task ShouldBeCaughtUpAsync(IAsyncEnumerator<SubscriptionMessage> enumerator)
    {
        (await enumerator.MoveNextAsync()).ShouldBeTrue();
        enumerator.Current.ShouldBeOfType<SubscriptionMessage.CaughtUp>();
    }

    private static async Task<RecoveredEvents> ReadUntilRecoveredAsync(IAsyncEnumerator<SubscriptionMessage> enumerator)
    {
        var events = new List<TestEvent>();
        var fellBehindCount = 0;
        var caughtUpCount = 0;

        while (fellBehindCount == 0 || caughtUpCount < fellBehindCount)
        {
            (await enumerator.MoveNextAsync()).ShouldBeTrue();

            switch (enumerator.Current)
            {
                case SubscriptionMessage.Event<ReadEvent<object, string, TStreamPos, TLogPos>> message:
                    events.Add(message.Value.Payload.ShouldBeOfType<TestEvent>());
                    break;
                case SubscriptionMessage.CaughtUp:
                    caughtUpCount++;
                    break;
                case SubscriptionMessage.FellBehind:
                    fellBehindCount++;
                    break;
            }
        }

        return new RecoveredEvents([.. events], fellBehindCount);
    }

    private sealed class RecoveredEvents(TestEvent[] events, int fellBehindCount)
    {
        public TestEvent[] Events { get; } = events;
        public int FellBehindCount { get; } = fellBehindCount;
    }
}