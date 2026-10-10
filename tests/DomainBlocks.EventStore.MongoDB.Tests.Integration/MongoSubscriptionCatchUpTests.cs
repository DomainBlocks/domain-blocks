using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.MongoDB.ChangeStreams;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.MongoDB.Bson;
using DomainBlocks.Testing.Events;
using DomainBlocks.Testing.Integration.EventStore;
using DomainBlocks.Testing.Integration.EventStore.MongoDB;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

using Event = ReadEvent<object, string, StreamPosition, LogPosition>;
using Message = SubscriptionMessage<object, string, StreamPosition, LogPosition>;

/// <summary>
/// How a subscription replays the log when it has to restart. The subscription is built from its parts, as the store
/// builds it, so that the test can see each replay that it asks the reader for and hold one back part-way.
/// </summary>
[TestFixture]
public class MongoSubscriptionCatchUpTests
{
    private readonly List<Replay> _replays = [];
    private MongoEventStoreOptions _options = null!;
    private IEventStore<object, string, StreamPosition, LogPosition> _eventStore = null!;
    private EventLogReader<object> _reader = null!;
    private RefCountedChangeStreamSubject<EventLogDocument<object>> _subject = null!;
    private int _heldReplayIndex;
    private TaskCompletionSource? _releaseHeldReplay;

    [SetUp]
    public async Task SetUp()
    {
        _options = new MongoEventStoreOptions { DatabaseName = $"catchup_{Guid.NewGuid():N}" };
        await MongoEventStoreAdmin.EnsureInitializedAsync(MongoTestEnvironment.MongoClient, _options);

        var typeMap = new EventTypeMapBuilder().Add<TestEvent>().Build();

        var codec = EventCodec.Create(new EventCodecOptions<object, BsonValue, BsonValue>
        {
            TypeMap = typeMap,
            EventSerializer = new BsonDocumentObjectSerializer(),
            MetadataSerializer = new BsonDocumentMetadataSerializer()
        });

        _eventStore = new MongoEventStoreBuilder<object>()
            .UseClient(MongoTestEnvironment.MongoClient)
            .UseOptions(_options)
            .ConfigureCodec(x => x.UseEventTypeMap(typeMap).UseEventSerializer(new BsonDocumentObjectSerializer()))
            .Build();

        var eventLog = MongoTestEnvironment.MongoClient
            .GetDatabase(_options.DatabaseName)
            .GetCollection<BsonDocument>(_options.EventLogCollectionName)
            .WithReadConcern(ReadConcern.Majority)
            .WithReadPreference(ReadPreference.Primary);

        var insertsOnly = Builders<ChangeStreamDocument<BsonDocument>>.Filter.Eq(
            x => x.OperationType,
            ChangeStreamOperationType.Insert);

        var liveDocument = new EventLogDocument<object>(codec);

        _replays.Clear();
        _releaseHeldReplay = null;
        _reader = new EventLogReader<object>(eventLog, codec);

        _subject = RefCountedChangeStreamSubject.Create(
            eventLog.Database.Client,
            eventLog.WatchAsync,
            new EmptyPipelineDefinition<ChangeStreamDocument<BsonDocument>>().Match(insertsOnly),
            static change => change.ResumeToken,
            change =>
            {
                liveDocument.Set(change.FullDocument);
                return liveDocument;
            },
            logger: MongoTestEnvironment.LoggerFactory.CreateLogger("ChangeStreamSubject"));
    }

    [TearDown]
    public async Task TearDown()
    {
        await _eventStore.DisposeAsync();
        await MongoTestEnvironment.MongoClient.DropDatabaseAsync(_options.DatabaseName);
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task SubscribeToAll_WhenQueueOverflowsDuringReplay_FinishesReplayBeforeRestarting(CancellationToken ct)
    {
        var existing = await AppendEventsAsync("existing", 3, ct);
        var release = HoldReplayAfterItsFirstEvent(replayIndex: 0);

        await using var enumerator = SubscribeToAll(new SubscriptionOptions { QueueCapacity = 1 })
            .GetAsyncEnumerator(ct);

        // The replay delivers its first event and is then held, with the rest still to come.
        (await NextMessageAsync(enumerator)).Event!.Value.Payload.ShouldBe(existing[0]);
        await using var witness = await CreateWitnessAsync(ct);
        var pending = enumerator.MoveNextAsync();

        // More events arrive live than the queue holds, so the subscription has to restart.
        var live = await AppendEventsAsync("live", 2, ct);

        await ReadEventsAsync(witness, live.Length);

        release.SetResult();
        (await pending).ShouldBeTrue();

        var messages = new List<Message> { enumerator.Current };

        while (!messages[^1].IsCaughtUp)
            messages.Add(await NextMessageAsync(enumerator));

        // The first replay ran to its high-water mark, and the second went on from there.
        _replays.Count.ShouldBe(2);
        _replays[0].ShouldBe(new Replay(AfterExclusive: -1, HighWaterMark: 2, Completed: true));
        _replays[1].ShouldBe(new Replay(AfterExclusive: 2, HighWaterMark: 4, Completed: true));

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
        var existing = await AppendEventsAsync("existing", 1, ct);
        var release = HoldReplayAfterItsFirstEvent(replayIndex: 1);

        await using var enumerator = SubscribeToAll(new SubscriptionOptions { QueueCapacity = 1 })
            .GetAsyncEnumerator(ct);

        (await NextMessageAsync(enumerator)).Event!.Value.Payload.ShouldBe(existing[0]);
        (await NextMessageAsync(enumerator)).IsCaughtUp.ShouldBeTrue();

        // The queue overflows while the subscriber is caught up, so it is told that it fell behind.
        TestEvent[] first;

        await using (var witness = await CreateWitnessAsync(ct))
        {
            first = await AppendEventsAsync("first", 3, ct);
            await ReadEventsAsync(witness, first.Length);
        }

        (await NextMessageAsync(enumerator)).Event!.Value.Payload.ShouldBe(first[0]);
        (await NextMessageAsync(enumerator)).IsFellBehind.ShouldBeTrue();

        // The replay that follows delivers its first event and is then held, with the rest still to come.
        (await NextMessageAsync(enumerator)).Event!.Value.Payload.ShouldBe(first[1]);
        var pending = enumerator.MoveNextAsync();

        // The queue overflows again before the subscriber has caught up again. The subscription attached to the change
        // stream again when it restarted, so it takes another witness to follow it.
        await using var secondWitness = await CreateWitnessAsync(ct);
        var second = await AppendEventsAsync("second", 2, ct);
        await ReadEventsAsync(secondWitness, second.Length);

        release.SetResult();
        (await pending).ShouldBeTrue();

        var messages = new List<Message> { enumerator.Current };

        while (!messages[^1].IsCaughtUp)
            messages.Add(await NextMessageAsync(enumerator));

        // It took a third replay to catch up, and the subscriber was not told a second time that it fell behind.
        _replays.Count.ShouldBe(3);

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
    public async Task SubscribeToAll_WhenRestartingAfterReplayThatEndedOnEventsFilterExcludes_ResumesFromHighWaterMark(
        CancellationToken ct)
    {
        // The filter selects the first of the existing events and not the two that follow it.
        var selected = await AppendEventsAsync("existing", 1, ct, "selected");
        await AppendEventsAsync("existing", 2, ct, "excluded");
        var release = HoldReplayAfterItsFirstEvent(replayIndex: 0);

        await using var enumerator = SubscribeToAll(
                new SubscriptionOptions { QueueCapacity = 1 },
                Builders<BsonDocument>.Filter.Eq(EventLogEntry.FieldNames.StreamId, "selected"),
                EventFilter.StreamIds("selected"))
            .GetAsyncEnumerator(ct);

        // The replay delivers the event that the filter selects and is then held.
        (await NextMessageAsync(enumerator)).Event!.Value.Payload.ShouldBe(selected[0]);
        await using var witness = await CreateWitnessAsync(ct);
        var pending = enumerator.MoveNextAsync();

        // More events that the filter selects arrive live than the queue holds, so the subscription has to restart.
        var live = await AppendEventsAsync("live", 2, ct, "selected");
        await ReadEventsAsync(witness, live.Length);

        release.SetResult();
        (await pending).ShouldBeTrue();

        var messages = new List<Message> { enumerator.Current };

        while (!messages[^1].IsCaughtUp)
            messages.Add(await NextMessageAsync(enumerator));

        // The first replay delivered nothing after its first event, but it covered the log up to its high-water mark,
        // so the second went on from there rather than from the last event that was delivered.
        _replays.Count.ShouldBe(2);
        _replays[0].ShouldBe(new Replay(AfterExclusive: -1, HighWaterMark: 2, Completed: true));
        _replays[1].ShouldBe(new Replay(AfterExclusive: 2, HighWaterMark: 4, Completed: true));

        messages
            .Where(x => x.Event is not null)
            .Select(x => x.Event!.Value.Payload)
            .ShouldBe([live[0], live[1]]);
    }

    // The subscription under test, from the start of the log, with each of its replays recorded. The filters stand in
    // for a subscription filter, which the store does not take yet.
    private SubscriptionAsyncEnumerable<object, LogPosition> SubscribeToAll(
        SubscriptionOptions options,
        FilterDefinition<BsonDocument>? catchUpFilter = null,
        EventFilter? liveFilter = null)
    {
        var filter = catchUpFilter ?? Builders<BsonDocument>.Filter.Empty;

        return Subscribe(
            (reader, session, after, highWaterMark, ct) => RecordAsync(
                reader.ReadCatchUpAsync(session, filter, EventLogEntry.FieldNames.Position, after, highWaterMark, ct),
                after,
                highWaterMark,
                ct),
            liveFilter ?? EventFilter.All,
            SubscriptionOrigin.Start,
            options);
    }

    // A second subscription to the same change stream, from the end of the log, used to tell when the change stream
    // has given all the appended events to the subscription under test. The change stream gives each event to its
    // subscriptions in the order in which they attached to it, so a witness is created once the subscription under
    // test has attached.
    private async Task<IAsyncEnumerator<Message>> CreateWitnessAsync(CancellationToken ct)
    {
        var witness = Subscribe(
            static (reader, session, after, highWaterMark, ct) => reader.ReadCatchUpAsync(
                session,
                Builders<BsonDocument>.Filter.Empty,
                EventLogEntry.FieldNames.Position,
                after,
                highWaterMark,
                ct),
            EventFilter.All,
            SubscriptionOrigin.End,
            new SubscriptionOptions()).GetAsyncEnumerator(ct);

        (await NextMessageAsync(witness)).IsCaughtUp.ShouldBeTrue();

        return witness;
    }

    private SubscriptionAsyncEnumerable<object, LogPosition> Subscribe(
        SubscriptionAsyncEnumerable<object, LogPosition>.CatchUpReader catchUpReader,
        EventFilter liveFilter,
        SubscriptionOrigin<LogPosition> origin,
        SubscriptionOptions options)
    {
        return new SubscriptionAsyncEnumerable<object, LogPosition>(
            _reader,
            _subject,
            catchUpReader,
            static async (reader, session, ct) => await reader.GetLastPositionAsync(
                session,
                Builders<BsonDocument>.Filter.Empty,
                EventLogEntry.FieldNames.Position,
                ct) is { } pos
                ? LogPosition.FromInt64(pos)
                : null,
            liveFilter,
            static ctx => ctx.LogPosition,
            origin,
            options,
            MongoTestEnvironment.LoggerFactory.CreateLogger("Subscription"));
    }

    // Passes a replay through, noting what was asked for and whether it was read to its end.
    private async IAsyncEnumerable<Event> RecordAsync(
        IAsyncEnumerable<Event> events,
        long afterExclusive,
        long highWaterMark,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var index = _replays.Count;
        _replays.Add(new Replay(afterExclusive, highWaterMark, Completed: false));

        var isFirstEvent = true;

        await foreach (var e in events.WithCancellation(cancellationToken))
        {
            yield return e;

            if (isFirstEvent && index == _heldReplayIndex && _releaseHeldReplay is { } release)
                await release.Task.WaitAsync(cancellationToken);

            isFirstEvent = false;
        }

        _replays[index] = _replays[index] with { Completed = true };
    }

    // Holds the given replay once it has delivered its first event, until the returned source is completed.
    private TaskCompletionSource HoldReplayAfterItsFirstEvent(int replayIndex)
    {
        _heldReplayIndex = replayIndex;
        _releaseHeldReplay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        return _releaseHeldReplay;
    }

    private async Task<TestEvent[]> AppendEventsAsync(
        string prefix,
        int count,
        CancellationToken ct,
        string streamId = "s1")
    {
        var events = Enumerable.Range(0, count).Select(i => new TestEvent { Value = $"{prefix}-{i}" }).ToArray();

        foreach (var e in events)
            await _eventStore.AppendAsync(streamId, [AppendableEvent.Create<object>(e)], cancellationToken: ct);

        return events;
    }

    private static async Task ReadEventsAsync(IAsyncEnumerator<Message> enumerator, int count)
    {
        for (var i = 0; i < count; i++)
            (await enumerator.MoveNextAsync()).ShouldBeTrue();
    }

    private static async Task<Message> NextMessageAsync(IAsyncEnumerator<Message> enumerator)
    {
        (await enumerator.MoveNextAsync()).ShouldBeTrue();

        return enumerator.Current;
    }

    private sealed record Replay(long AfterExclusive, long HighWaterMark, bool Completed);
}