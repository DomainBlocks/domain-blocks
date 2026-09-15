using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Metadata;
using DomainBlocks.EventStore.Pipeline;
using DomainBlocks.EventStore.Transforms;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Unit.Pipeline;

public class EventStorePipelineTests
{
    private FakeEventStore _inner = null!;

    [SetUp]
    public void SetUp() => _inner = new FakeEventStore();

    [Test]
    public void WithPipeline_NoStages_ReturnsInnerStore()
    {
        _inner.WithPipeline(_ => { }).ShouldBeSameAs(_inner);
    }

    [Test]
    public async Task AppendAsync_NoContributors_PassesEventsThroughUntouched()
    {
        var store = _inner.WithPipeline(p => p.Transform(new SplitTransform()));
        AppendableEvent<object>[] events = [AppendableEvent.Create<object>(new Legacy("a"))];

        await store.AppendAsync("s", events);

        _inner.LastAppendedEnumerable.ShouldBeSameAs(events);
    }

    [Test]
    public async Task AppendAsync_WithContributors_MergesContributedAndExplicitMetadata()
    {
        var store = _inner.WithPipeline(p => p.ContributeMetadata(new FixedContributor("source", "pipeline")));

        await store.AppendAsync("s", [AppendableEvent.Create<object>(new Current("a"), [new("user", "bob")])]);

        var appended = _inner.Appends.ShouldHaveSingleItem().Events.ShouldHaveSingleItem();
        appended.Payload.ShouldBe(new Current("a"));
        appended.Metadata.ToArray().ShouldBe([new("source", "pipeline"), new("user", "bob")]);
    }

    [Test]
    public async Task AppendAsync_ExplicitMetadata_OverridesContributedEntryWithSameKey()
    {
        var store = _inner.WithPipeline(p => p.ContributeMetadata(new FixedContributor("user", "system")));

        await store.AppendAsync("s", [AppendableEvent.Create<object>(new Current("a"), [new("user", "bob")])]);

        _inner.Appends.Single().Events.Single().Metadata.ToArray().ShouldBe([new("user", "bob")]);
    }

    [Test]
    public async Task AppendAsync_LaterContributorSet_OverridesEarlierContributor()
    {
        var store = _inner.WithPipeline(p => p.ContributeMetadata(
            new FixedContributor("k", "first"),
            new FixedContributor("k", "second")));

        await store.AppendAsync("s", [AppendableEvent.Create<object>(new Current("a"))]);

        _inner.Appends.Single().Events.Single().Metadata.ToArray().ShouldBe([new("k", "second")]);
    }

    [Test]
    public async Task AppendAsync_ContributorTryAdd_DoesNotOverrideExistingKey()
    {
        var store = _inner.WithPipeline(p => p.ContributeMetadata(
            new FixedContributor("k", "first"),
            new TryAddContributor("k", "second"),
            new TryAddContributor("other", "added")));

        await store.AppendAsync("s", [AppendableEvent.Create<object>(new Current("a"))]);

        _inner.Appends.Single().Events.Single().Metadata.ToArray()
            .ShouldBe([new("k", "first"), new("other", "added")]);
    }

    [Test]
    public async Task AppendAsync_ManyEvents_EachEventKeepsItsOwnMetadataSlice()
    {
        // Enough entries to force the shared buffer to grow several times.
        const int eventCount = 100;
        var store = _inner.WithPipeline(p => p.ContributeMetadata(new IndexContributor()));

        var events = Enumerable.Range(0, eventCount)
            .Select(i => AppendableEvent.Create<object>(new Current($"e{i}"), [new("explicit", $"x{i}")]))
            .ToArray();

        await store.AppendAsync("s", events);

        var appended = _inner.Appends.Single().Events;
        appended.Length.ShouldBe(eventCount);

        for (var i = 0; i < eventCount; i++)
        {
            appended[i].Payload.ShouldBe(new Current($"e{i}"));
            appended[i].Metadata.ToArray().ShouldBe([new("index", i.ToString()), new("explicit", $"x{i}")]);
        }
    }

    [Test]
    public async Task ReadStream_EventWithoutTransform_PassesThrough()
    {
        _inner.ReadEvents.Add(FakeEventStore.ReadEventAt(new Current("a"), 0));
        var store = _inner.WithPipeline(p => p.Transform(new SplitTransform()));

        var events = await store.ReadStream("s").ToArrayAsync();

        events.ShouldHaveSingleItem().Payload.ShouldBe(new Current("a"));
    }

    [Test]
    public async Task ReadStream_WithTransform_ExpandsEventAndPreservesSourceContext()
    {
        var metadata = new Dictionary<string, string> { ["user"] = "bob" };
        _inner.ReadEvents.Add(FakeEventStore.ReadEventAt(new Legacy("a;b"), 7, metadata));
        var store = _inner.WithPipeline(p => p.Transform(new SplitTransform()));

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
        var store = _inner.WithPipeline(p => p.Transform(recorder));

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
        var store = _inner.WithPipeline(p => p.Transform(new OlderTransform(), new SplitTransform()));

        var events = await store.ReadAll().ToArrayAsync();

        events.Select(x => x.Payload).ShouldBe([new Current("x"), new Current("y"), new Current("z")]);
    }

    [Test]
    public async Task ReadStream_TransformDropsEvent_Throws()
    {
        _inner.ReadEvents.Add(FakeEventStore.ReadEventAt(new Legacy(""), 0));
        var store = _inner.WithPipeline(p => p.Transform(new SplitTransform()));

        var ex = await Should.ThrowAsync<InvalidOperationException>(() => store.ReadStream("s").ToArrayAsync().AsTask());

        ex.Message.ShouldContain("AllowDroppingEvents");
    }

    [Test]
    public async Task ReadStream_TransformDropsEvent_WhenAllowed_DropsIt()
    {
        _inner.ReadEvents.Add(FakeEventStore.ReadEventAt(new Legacy(""), 0));
        _inner.ReadEvents.Add(FakeEventStore.ReadEventAt(new Current("kept"), 1));
        var store = _inner.WithPipeline(p => p.Transform(new SplitTransform()).AllowDroppingEvents());

        var events = await store.ReadStream("s").ToArrayAsync();

        events.ShouldHaveSingleItem().Payload.ShouldBe(new Current("kept"));
    }

    [Test]
    public async Task ReadStream_TransformProducesItsOwnSourceType_Throws()
    {
        _inner.ReadEvents.Add(FakeEventStore.ReadEventAt(new Legacy("a"), 0));
        var store = _inner.WithPipeline(p => p.Transform(new IdentityTransform()));

        await Should.ThrowAsync<InvalidOperationException>(() => store.ReadStream("s").ToArrayAsync().AsTask());
    }

    [Test]
    public async Task SubscribeToAll_TransformsEventsAndPassesOtherMessagesByReference()
    {
        var caughtUp = new SubscriptionMessage.CaughtUp();
        var untouched = SubscriptionMessage.Event.Create(FakeEventStore.ReadEventAt(new Current("c"), 1));
        _inner.SubscriptionMessages.Add(SubscriptionMessage.Event.Create(FakeEventStore.ReadEventAt(new Legacy("a;b"), 0)));
        _inner.SubscriptionMessages.Add(caughtUp);
        _inner.SubscriptionMessages.Add(untouched);
        var store = _inner.WithPipeline(p => p.Transform(new SplitTransform()));

        var messages = await store.SubscribeToAll().ToArrayAsync();

        messages.Length.ShouldBe(4);
        Payload(messages[0]).ShouldBe(new Current("a"));
        Payload(messages[1]).ShouldBe(new Current("b"));
        messages[2].ShouldBeSameAs(caughtUp);
        messages[3].ShouldBeSameAs(untouched);
    }

    [Test]
    public async Task SubscribeToStream_NoTransforms_ReturnsInnerEnumerable()
    {
        var store = _inner.WithPipeline(p => p.ContributeMetadata(new FixedContributor("k", "v")));

        _inner.SubscriptionMessages.Add(new SubscriptionMessage.CaughtUp());

        var messages = await store.SubscribeToStream("s").ToArrayAsync();
        messages.ShouldHaveSingleItem().ShouldBeOfType<SubscriptionMessage.CaughtUp>();
    }

    [Test]
    public void WithPipeline_DuplicateSourceType_Throws()
    {
        Should.Throw<ArgumentException>(() =>
            _inner.WithPipeline(p => p.Transform(new SplitTransform(), new IdentityTransform())));
    }

    [Test]
    public async Task DisposeAsync_ForwardsToInnerStore()
    {
        var store = _inner.WithPipeline(p => p.ContributeMetadata(new FixedContributor("k", "v")));

        await store.ShouldBeAssignableTo<IAsyncDisposable>()!.DisposeAsync();

        _inner.Disposed.ShouldBeTrue();
    }

    private static object Payload(SubscriptionMessage message) =>
        message.ShouldBeOfType<SubscriptionMessage.Event<ReadEvent<object, string, StreamPosition, LogPosition>>>()
            .Value.Payload;

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

    private sealed class IndexContributor : IMetadataContributor<object>
    {
        private int _index;

        public void Contribute(object @event, MetadataWriter metadata) => metadata.Set("index", (_index++).ToString());
    }
}