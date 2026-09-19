using DomainBlocks.EventStore.Metadata;
using DomainBlocks.EventStore.Transforms;
using NUnit.Framework;
using Shouldly;
using Message = DomainBlocks.EventStore.SubscriptionMessage<
    object,
    string,
    DomainBlocks.EventStore.StreamPosition,
    DomainBlocks.EventStore.LogPosition>;

namespace DomainBlocks.EventStore.Tests.Unit.Decoration;

public class EventStoreDecoratorTests
{
    private FakeEventStore _inner = null!;

    [SetUp]
    public void SetUp() => _inner = new FakeEventStore();

    [Test]
    public void WithMetadataContributors_None_ReturnsInnerStore()
    {
        _inner.WithMetadataContributors().ShouldBeSameAs(_inner);
    }

    [Test]
    public void WithReadTransforms_None_ReturnsInnerStore()
    {
        _inner.WithReadTransforms().ShouldBeSameAs(_inner);
    }

    [Test]
    public async Task EnsureInitializedAsync_ForwardsToInnerStore()
    {
        var store = _inner
            .WithMetadataContributors(new FixedContributor("k", "v"))
            .WithReadTransforms(new SplitTransform());

        store.ShouldNotBeSameAs(_inner);

        await store.EnsureInitializedAsync();

        _inner.InitializeCalls.ShouldBe(1);
    }

    [Test]
    public async Task WithMetadataContributors_ThenWithReadTransforms_AppliesBothInEitherOrder()
    {
        _inner.ReadEvents.Add(FakeEventStore.ReadEventAt(new Legacy("a;b"), 0));

        IEventStore<object, string, StreamPosition, LogPosition>[] stores =
        [
            _inner.WithMetadataContributors(new FixedContributor("k", "v")).WithReadTransforms(new SplitTransform()),
            _inner.WithReadTransforms(new SplitTransform()).WithMetadataContributors(new FixedContributor("k", "v"))
        ];

        foreach (var store in stores)
        {
            await store.AppendAsync("s", [AppendableEvent.Create<object>(new Current("x"))]);
            _inner.Appends[^1].Events.Single().Metadata.ShouldBe([new("k", "v")]);

            var events = await store.ReadStream("s").ToArrayAsync();
            events.Select(x => x.Payload).ShouldBe([new Current("a"), new Current("b")]);
        }
    }

    [Test]
    public async Task WithReadTransforms_Twice_MergesTransforms()
    {
        _inner.ReadEvents.Add(FakeEventStore.ReadEventAt(new Older("x;y", "z"), 0));

        var store = _inner
            .WithReadTransforms(new OlderTransform())
            .WithReadTransforms(new SplitTransform());

        var events = await store.ReadStream("s").ToArrayAsync();

        events.Select(x => x.Payload).ShouldBe([new Current("x"), new Current("y"), new Current("z")]);
    }

    [Test]
    public async Task AppendAsync_NoContributors_PassesEventsThroughUntouched()
    {
        var store = _inner.WithReadTransforms(new SplitTransform());
        AppendableEvent<object>[] events = [AppendableEvent.Create<object>(new Legacy("a"))];

        await store.AppendAsync("s", events);

        _inner.LastAppendedEnumerable.ShouldBeSameAs(events);
    }

    [Test]
    public async Task AppendAsync_WithContributors_MergesContributedAndExplicitMetadata()
    {
        var store = _inner.WithMetadataContributors(new FixedContributor("source", "pipeline"));

        await store.AppendAsync("s", [AppendableEvent.Create<object>(new Current("a"), [new("user", "bob")])]);

        var appended = _inner.Appends.ShouldHaveSingleItem().Events.ShouldHaveSingleItem();
        appended.Payload.ShouldBe(new Current("a"));
        appended.Metadata.ShouldBe([new("source", "pipeline"), new("user", "bob")]);
    }

    [Test]
    public async Task AppendAsync_ExplicitMetadata_OverridesContributedEntryWithSameKey()
    {
        var store = _inner.WithMetadataContributors(new FixedContributor("user", "system"));

        await store.AppendAsync("s", [AppendableEvent.Create<object>(new Current("a"), [new("user", "bob")])]);

        _inner.Appends.Single().Events.Single().Metadata.ShouldBe([new("user", "bob")]);
    }

    [Test]
    public async Task AppendAsync_LaterContributorSet_OverridesEarlierContributor()
    {
        var store = _inner.WithMetadataContributors(
            new FixedContributor("k", "first"),
            new FixedContributor("k", "second"));

        await store.AppendAsync("s", [AppendableEvent.Create<object>(new Current("a"))]);

        _inner.Appends.Single().Events.Single().Metadata.ShouldBe([new("k", "second")]);
    }

    [Test]
    public async Task AppendAsync_ContributorTryAdd_DoesNotOverrideExistingKey()
    {
        var store = _inner.WithMetadataContributors(
            new FixedContributor("k", "first"),
            new TryAddContributor("k", "second"),
            new TryAddContributor("other", "added"));

        await store.AppendAsync("s", [AppendableEvent.Create<object>(new Current("a"))]);

        _inner.Appends.Single().Events.Single().Metadata
            .ShouldBe([new("k", "first"), new("other", "added")]);
    }

    [Test]
    public async Task AppendAsync_ManyEvents_EachEventKeepsItsOwnMetadataSlice()
    {
        // Enough entries to span several pooled chunks.
        const int eventCount = 1000;
        var store = _inner.WithMetadataContributors(new IndexContributor());

        var events = Enumerable.Range(0, eventCount)
            .Select(i => AppendableEvent.Create<object>(new Current($"e{i}"), [new("explicit", $"x{i}")]))
            .ToArray();

        await store.AppendAsync("s", events);

        var appended = _inner.Appends.Single().Events;
        appended.Length.ShouldBe(eventCount);

        for (var i = 0; i < eventCount; i++)
        {
            appended[i].Payload.ShouldBe(new Current($"e{i}"));
            appended[i].Metadata.ShouldBe([new("index", i.ToString()), new("explicit", $"x{i}")]);
        }
    }

    [Test]
    public async Task AppendAsync_EventWithMoreEntriesThanAChunk_KeepsAllEntries()
    {
        const int entryCount = 700;
        var store = _inner.WithMetadataContributors(new WideContributor(entryCount));

        await store.AppendAsync("s", [
            AppendableEvent.Create<object>(new Current("before")),
            AppendableEvent.Create<object>(new Current("wide"), [new("explicit", "x")]),
            AppendableEvent.Create<object>(new Current("after"))
        ]);

        var appended = _inner.Appends.Single().Events;
        appended.Length.ShouldBe(3);

        foreach (var e in appended)
        {
            e.Metadata.Take(entryCount).ShouldBe(Enumerable.Range(0, entryCount)
                .Select(i => new KeyValuePair<string, string>($"k{i}", $"v{i}")));
        }

        appended[1].Metadata.Length.ShouldBe(entryCount + 1);
        appended[1].Metadata[^1].ShouldBe(new KeyValuePair<string, string>("explicit", "x"));
    }

    [Test]
    public async Task AppendAsync_TwoBatches_DoNotShareBuffers()
    {
        var store = _inner.WithMetadataContributors(new IndexContributor());

        await store.AppendAsync("s", [AppendableEvent.Create<object>(new Current("a"))]);
        await store.AppendAsync("s", [AppendableEvent.Create<object>(new Current("b"))]);

        _inner.Appends[0].Events.Single().Metadata.ShouldBe([new("index", "0")]);
        _inner.Appends[1].Events.Single().Metadata.ShouldBe([new("index", "1")]);
    }

    [Test]
    public async Task ReadStream_EventWithoutTransform_PassesThrough()
    {
        _inner.ReadEvents.Add(FakeEventStore.ReadEventAt(new Current("a"), 0));
        var store = _inner.WithReadTransforms(new SplitTransform());

        var events = await store.ReadStream("s").ToArrayAsync();

        events.ShouldHaveSingleItem().Payload.ShouldBe(new Current("a"));
    }

    [Test]
    public async Task ReadStream_WithTransform_ExpandsEventAndPreservesSourceContext()
    {
        var metadata = new Dictionary<string, string> { ["user"] = "bob" };
        _inner.ReadEvents.Add(FakeEventStore.ReadEventAt(new Legacy("a;b"), 7, metadata));
        var store = _inner.WithReadTransforms(new SplitTransform());

        var events = await store.ReadStream("s").ToArrayAsync();

        events.Select(x => x.Payload).ShouldBe([new Current("a"), new Current("b")]);

        foreach (var e in events)
        {
            e.Context.StreamPosition.ShouldBe(new StreamPosition(7));
            e.Context.LogPosition.ShouldBe(new LogPosition(7));
            e.Context.Metadata.ShouldBeSameAs(metadata);
        }
    }

    [Test]
    public async Task ReadStream_TransformReceivesMetadataAndCreatedAt()
    {
        var metadata = new Dictionary<string, string> { ["user"] = "bob" };
        var readEvent = FakeEventStore.ReadEventAt(new Legacy("a"), 3, metadata);
        _inner.ReadEvents.Add(readEvent);
        var recorder = new RecordingTransform();
        var store = _inner.WithReadTransforms(recorder);

        await store.ReadStream("s").ToArrayAsync();

        var info = recorder.Infos.ShouldHaveSingleItem();
        info.Metadata.ShouldBeSameAs(metadata);
        info.CreatedAt.ShouldBe(readEvent.Context.CreatedAt);
    }

    [Test]
    public async Task ReadAll_ChainedTransforms_ProduceDepthFirstOrder()
    {
        // Older -> Legacy("x;y") -> Current("x"), Current("y"); a sibling Current("z") must come after both.
        _inner.ReadEvents.Add(FakeEventStore.ReadEventAt(new Older("x;y", "z"), 0));
        var store = _inner.WithReadTransforms(new OlderTransform(), new SplitTransform());

        var events = await store.ReadAll().ToArrayAsync();

        events.Select(x => x.Payload).ShouldBe([new Current("x"), new Current("y"), new Current("z")]);
    }

    [Test]
    public async Task ReadStream_TransformIgnoresEventWithoutIgnoredEventInstance_Throws()
    {
        _inner.ReadEvents.Add(FakeEventStore.ReadEventAt(new Legacy(""), 0));
        var store = _inner.WithReadTransforms(new SplitTransform());

        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            store.ReadStream("s").ToArrayAsync().AsTask());

        ex.Message.ShouldContain("no ignored-event sentinel is configured");
    }

    [Test]
    public async Task ReadStream_TransformIgnoresEventWithIgnoredEventInstance_EmitsIgnoredEventWithSourceContext()
    {
        var metadata = new Dictionary<string, string> { ["user"] = "bob" };
        _inner.ReadEvents.Add(FakeEventStore.ReadEventAt(new Legacy(""), 4, metadata));
        _inner.ReadEvents.Add(FakeEventStore.ReadEventAt(new Current("kept"), 5));
        var store = _inner.WithReadTransforms(new SplitTransform()).UseIgnoredEventSentinel(IgnoredEvent.Instance);

        var events = await store.ReadStream("s").ToArrayAsync();

        events.Length.ShouldBe(2);
        events[0].Payload.ShouldBeSameAs(IgnoredEvent.Instance);
        events[0].Context.StreamPosition.ShouldBe(new StreamPosition(4));
        events[0].Context.Metadata.ShouldBeSameAs(metadata);
        events[1].Payload.ShouldBe(new Current("kept"));
    }

    [Test]
    public async Task ReadStream_IgnoredTailEventWithIgnoredEventInstance_StillObservesLastPosition()
    {
        _inner.ReadEvents.Add(FakeEventStore.ReadEventAt(new Current("a"), 0));
        _inner.ReadEvents.Add(FakeEventStore.ReadEventAt(new Legacy(""), 1));
        var store = _inner.WithReadTransforms(new SplitTransform()).UseIgnoredEventSentinel(IgnoredEvent.Instance);

        var events = await store.ReadStream("s").ToArrayAsync();

        events[^1].Context.StreamPosition.ShouldBe(new StreamPosition(1));
    }

    [Test]
    public async Task SubscribeToAll_IgnoredEventWithIgnoredEventInstance_EmitsIgnoredEventMessage()
    {
        _inner.SubscriptionMessages.Add(FakeEventStore.ReadEventAt(new Legacy(""), 9));
        var store = _inner.WithReadTransforms(new SplitTransform()).UseIgnoredEventSentinel(IgnoredEvent.Instance);

        var messages = await store.SubscribeToAll().ToArrayAsync();

        var e = messages.ShouldHaveSingleItem().Value.ShouldBeOfType<ReadEvent<object, string, StreamPosition, LogPosition>>();
        e.Payload.ShouldBeSameAs(IgnoredEvent.Instance);
        e.Context.LogPosition.ShouldBe(new LogPosition(9));
    }

    [Test]
    public void WithReadTransforms_IgnoredEventTypeHasTransform_Throws()
    {
        Should.Throw<ArgumentException>(() =>
            _inner.WithReadTransforms(new SplitTransform()).UseIgnoredEventSentinel(new Legacy("ignored")));
    }

    [Test]
    public async Task ReadStream_TransformProducesItsOwnSourceType_Throws()
    {
        _inner.ReadEvents.Add(FakeEventStore.ReadEventAt(new Legacy("a"), 0));
        var store = _inner.WithReadTransforms(new IdentityTransform());

        await Should.ThrowAsync<InvalidOperationException>(() => store.ReadStream("s").ToArrayAsync().AsTask());
    }

    [Test]
    public async Task SubscribeToAll_TransformsEventsAndPassesOtherMessagesThroughUnchanged()
    {
        var untouched = new Current("c");

        _inner.SubscriptionMessages.Add(
            FakeEventStore.ReadEventAt(new Legacy("a;b"), 0));

        _inner.SubscriptionMessages.Add(SubscriptionMessage.CaughtUp);
        _inner.SubscriptionMessages.Add(FakeEventStore.ReadEventAt(untouched, 1));
        var store = _inner.WithReadTransforms(new SplitTransform());

        var messages = await store.SubscribeToAll().ToArrayAsync();

        messages.Length.ShouldBe(4);
        Payload(messages[0]).ShouldBe(new Current("a"));
        Payload(messages[1]).ShouldBe(new Current("b"));
        messages[2].Value.ShouldBeOfType<SubscriptionCaughtUp>();
        Payload(messages[3]).ShouldBeSameAs(untouched);
    }

    [Test]
    public async Task SubscribeToStream_NoTransforms_ReturnsInnerEnumerable()
    {
        var store = _inner.WithMetadataContributors(new FixedContributor("k", "v"));

        _inner.SubscriptionMessages.Add(SubscriptionMessage.CaughtUp);

        var messages = await store.SubscribeToStream("s").ToArrayAsync();
        messages.ShouldHaveSingleItem().Value.ShouldBeOfType<SubscriptionCaughtUp>();
    }

    [Test]
    public void WithReadTransforms_DuplicateSourceType_Throws()
    {
        Should.Throw<ArgumentException>(() =>
            _inner.WithReadTransforms(new SplitTransform(), new IdentityTransform()));
    }

    [Test]
    public void WithReadTransforms_DuplicateSourceTypeAcrossCalls_Throws()
    {
        Should.Throw<ArgumentException>(() =>
            _inner.WithReadTransforms(new SplitTransform()).WithReadTransforms(new IdentityTransform()));
    }

    [Test]
    public async Task DisposeAsync_ForwardsToInnerStore()
    {
        var store = _inner.WithMetadataContributors(new FixedContributor("k", "v"));

        await store.DisposeAsync();

        _inner.Disposed.ShouldBeTrue();
    }

    private static object Payload(Message message) => message.Value.ShouldBeOfType<ReadEvent<object, string, StreamPosition, LogPosition>>().Payload;

    private sealed record Older(string Values, string Extra);

    private sealed record Legacy(string Values);

    private sealed record Current(string Value);

    private sealed class OlderTransform : ReadEventTransform<object, Older>
    {
        protected override IEnumerable<object> Apply(Older @event, ReadEventInfo info)
        {
            yield return new Legacy(@event.Values);
            yield return new Current(@event.Extra);
        }
    }

    private sealed class SplitTransform : ReadEventTransform<object, Legacy>
    {
        protected override IEnumerable<object> Apply(Legacy @event, ReadEventInfo info) =>
            @event.Values.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(x => new Current(x));
    }

    private sealed class IdentityTransform : ReadEventTransform<object, Legacy>
    {
        protected override IEnumerable<object> Apply(Legacy @event, ReadEventInfo info) => [@event];
    }

    private sealed class RecordingTransform : ReadEventTransform<object, Legacy>
    {
        public List<ReadEventInfo> Infos { get; } = [];

        protected override IEnumerable<object> Apply(Legacy @event, ReadEventInfo info)
        {
            Infos.Add(info);
            return [new Current(@event.Values)];
        }
    }

    private sealed class FixedContributor(string key, string value) : IMetadataContributor<object>
    {
        public void Contribute(object @event, MetadataWriter metadata) => metadata.Set(key, value);
    }

    private sealed class TryAddContributor(string key, string value) : IMetadataContributor<object>
    {
        public void Contribute(object @event, MetadataWriter metadata) => metadata.TryAdd(key, value);
    }

    private sealed class WideContributor(int entryCount) : IMetadataContributor<object>
    {
        public void Contribute(object @event, MetadataWriter metadata)
        {
            for (var i = 0; i < entryCount; i++)
                metadata.Set($"k{i}", $"v{i}");
        }
    }

    private sealed class IndexContributor : IMetadataContributor<object>
    {
        private int _index;

        public void Contribute(object @event, MetadataWriter metadata) => metadata.Set("index", _index++.ToString());
    }
}