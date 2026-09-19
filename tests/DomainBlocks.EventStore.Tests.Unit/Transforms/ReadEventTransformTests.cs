using DomainBlocks.EventStore.Tests.Unit.Decoration;
using DomainBlocks.EventStore.Transforms;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Unit.Transforms;

public class ReadEventTransformTests
{
    private FakeEventStore _inner = null!;

    [SetUp]
    public void SetUp() => _inner = new FakeEventStore();

    [Test]
    public void Create_SourceEventTypeIsTSourceEvent()
    {
        var transform = ReadEventTransform.Create<object, Legacy>(e => new Current(e.Values));

        transform.SourceEventType.ShouldBe(typeof(Legacy));
    }

    [Test]
    public void Create_NullFunc_Throws()
    {
        Should.Throw<ArgumentNullException>(() => ReadEventTransform.Create((Func<Legacy, object>)null!));

        Should.Throw<ArgumentNullException>(() =>
            ReadEventTransform.Create((Func<Legacy, ReadEventInfo, object>)null!));

        Should.Throw<ArgumentNullException>(() => ReadEventTransform.Create((Func<Legacy, IEnumerable<object>>)null!));

        Should.Throw<ArgumentNullException>(() =>
            ReadEventTransform.Create((Func<Legacy, ReadEventInfo, IEnumerable<object>>)null!));
    }

    [Test]
    public async Task Create_SingleEventFunc_ReplacesEventAndPreservesSourceContext()
    {
        var metadata = new Dictionary<string, string> { ["user"] = "bob" };
        _inner.ReadEvents.Add(FakeEventStore.ReadEventAt(new Legacy("a"), 7, metadata));
        var store = _inner.WithReadTransforms(ReadEventTransform.Create<object, Legacy>(e => new Current(e.Values)));

        var events = await store.ReadStream("s").ToArrayAsync();

        var readEvent = events.ShouldHaveSingleItem();
        readEvent.Payload.ShouldBe(new Current("a"));
        readEvent.Context.StreamPosition.ShouldBe(new StreamPosition(7));
        readEvent.Context.LogPosition.ShouldBe(new LogPosition(7));
        readEvent.Context.Metadata.ShouldBeSameAs(metadata);
    }

    [Test]
    public async Task Create_SingleEventFuncWithInfo_ReceivesMetadataAndCreatedAt()
    {
        var metadata = new Dictionary<string, string> { ["user"] = "bob" };
        var source = FakeEventStore.ReadEventAt(new Legacy("a"), 3, metadata);
        _inner.ReadEvents.Add(source);
        var infos = new List<ReadEventInfo>();

        var store = _inner.WithReadTransforms(ReadEventTransform.Create<object, Legacy>((e, info) =>
        {
            infos.Add(info);
            return new Current(e.Values);
        }));

        var events = await store.ReadStream("s").ToArrayAsync();

        events.ShouldHaveSingleItem().Payload.ShouldBe(new Current("a"));
        var received = infos.ShouldHaveSingleItem();
        received.Metadata.ShouldBeSameAs(metadata);
        received.CreatedAt.ShouldBe(source.Context.CreatedAt);
    }

    [Test]
    public async Task Create_SequenceFunc_ExpandsEvent()
    {
        _inner.ReadEvents.Add(FakeEventStore.ReadEventAt(new Legacy("a;b"), 0));

        var store = _inner.WithReadTransforms(
            ReadEventTransform.Create<object, Legacy>(e => [new Current(e.Values[..1]), new Current(e.Values[2..])]));

        var events = await store.ReadStream("s").ToArrayAsync();

        events.Select(x => x.Payload).ShouldBe([new Current("a"), new Current("b")]);
    }

    [Test]
    public async Task Create_SequenceFuncWithInfo_ReceivesMetadataAndCreatedAt()
    {
        var metadata = new Dictionary<string, string> { ["user"] = "bob" };
        var source = FakeEventStore.ReadEventAt(new Legacy("a"), 3, metadata);
        _inner.ReadEvents.Add(source);
        var infos = new List<ReadEventInfo>();

        var store = _inner.WithReadTransforms(ReadEventTransform.Create<object, Legacy>((e, info) =>
        {
            infos.Add(info);
            return [new Current(e.Values), new Current(e.Values)];
        }));

        var events = await store.ReadStream("s").ToArrayAsync();

        events.Select(x => x.Payload).ShouldBe([new Current("a"), new Current("a")]);
        var received = infos.ShouldHaveSingleItem();
        received.Metadata.ShouldBeSameAs(metadata);
        received.CreatedAt.ShouldBe(source.Context.CreatedAt);
    }

    [Test]
    public async Task Create_SequenceReturningFuncWithObjectBase_BindsToOneToMany()
    {
        // With object as the event base a sequence is itself a valid single event, so this pins that overload
        // resolution prefers the one-to-many form.
        _inner.ReadEvents.Add(FakeEventStore.ReadEventAt(new Legacy("a;b"), 0));

        var store = _inner.WithReadTransforms(
            ReadEventTransform.Create<object, Legacy>(e => e.Values.Split(';').Select(v => new Current(v))));

        var events = await store.ReadStream("s").ToArrayAsync();

        events.Select(x => x.Payload).ShouldBe([new Current("a"), new Current("b")]);
    }

    [Test]
    public async Task Create_SubscribeToAll_AppliesTransform()
    {
        _inner.SubscriptionMessages.Add(SubscriptionMessage.Event(FakeEventStore.ReadEventAt(new Legacy("a"), 9)));
        var store = _inner.WithReadTransforms(ReadEventTransform.Create<object, Legacy>(e => new Current(e.Values)));

        var messages = await store.SubscribeToAll().ToArrayAsync();

        var readEvent = messages.ShouldHaveSingleItem().Event.ShouldNotBeNull();
        readEvent.Payload.ShouldBe(new Current("a"));
        readEvent.Context.LogPosition.ShouldBe(new LogPosition(9));
    }

    [Test]
    public async Task Create_ChainsWithClassBasedTransform()
    {
        _inner.ReadEvents.Add(FakeEventStore.ReadEventAt(new Older("x"), 0));

        var store = _inner.WithReadTransforms(
            ReadEventTransform.Create<object, Older>(e => new Legacy(e.Values)),
            new LegacyTransform());

        var events = await store.ReadStream("s").ToArrayAsync();

        events.ShouldHaveSingleItem().Payload.ShouldBe(new Current("x"));
    }

    [Test]
    public async Task Create_SequenceFuncWithIgnoredEventWithoutIgnoredEventInstance_Throws()
    {
        _inner.ReadEvents.Add(FakeEventStore.ReadEventAt(new Legacy("a"), 0));
        var store = _inner.WithReadTransforms(ReadEventTransform.Create<object, Legacy>(_ => []));

        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            store.ReadStream("s").ToArrayAsync().AsTask());

        ex.Message.ShouldContain(nameof(Legacy));
        ex.Message.ShouldContain("no ignored-event sentinel is configured");
    }

    [Test]
    public async Task Create_SequenceFuncWithIgnoredEvent_ReadEmitsIgnoredEvent()
    {
        // Ignores on a condition taken from the info, so only one of the two source events is left out.
        var retired = new Dictionary<string, string> { ["retired"] = "true" };
        _inner.ReadEvents.Add(FakeEventStore.ReadEventAt(new Legacy("a"), 0, retired));
        _inner.ReadEvents.Add(FakeEventStore.ReadEventAt(new Legacy("b"), 1));

        var store = _inner
            .WithReadTransforms(ReadEventTransform.Create<object, Legacy>((e, info) =>
                info.Metadata.ContainsKey("retired") ? [] : [new Current(e.Values)]))
            .UseIgnoredEventSentinel(IgnoredEvent.Instance);

        var events = await store.ReadStream("s").ToArrayAsync();

        events.Select(x => x.Payload).ShouldBe([IgnoredEvent.Instance, new Current("b")]);
        events.Select(x => x.Context.StreamPosition).ShouldBe([new StreamPosition(0), new StreamPosition(1)]);
    }

    [Test]
    public async Task Create_SequenceFuncWithInfoIgnoresEvent_Throws()
    {
        _inner.ReadEvents.Add(FakeEventStore.ReadEventAt(new Legacy("a"), 0));
        var store = _inner.WithReadTransforms(ReadEventTransform.Create<object, Legacy>((_, _) => []));

        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            store.ReadStream("s").ToArrayAsync().AsTask());

        ex.Message.ShouldContain("no ignored-event sentinel is configured");
    }

    [Test]
    public async Task Create_SequenceFuncWithIgnoredEvent_SubscriptionEmitsPlaceholderMessage()
    {
        _inner.SubscriptionMessages.Add(SubscriptionMessage.Event(FakeEventStore.ReadEventAt(new Legacy("a"), 9)));

        var store = _inner
            .WithReadTransforms(ReadEventTransform.Create<object, Legacy>(_ => []))
            .UseIgnoredEventSentinel(IgnoredEvent.Instance);

        var messages = await store.SubscribeToAll().ToArrayAsync();

        var readEvent = messages.ShouldHaveSingleItem().Event.ShouldNotBeNull();
        readEvent.Payload.ShouldBeSameAs(IgnoredEvent.Instance);
        readEvent.Context.LogPosition.ShouldBe(new LogPosition(9));
    }

    [Test]
    public async Task Create_DerivedEventIsIgnoredByChainedFunc_EmitsPlaceholderInItsPlace()
    {
        // Older -> Legacy, Current; the Legacy is then ignored, and its placeholder keeps its place before Current.
        _inner.ReadEvents.Add(FakeEventStore.ReadEventAt(new Older("x"), 0));

        var store = _inner
            .WithReadTransforms(
                ReadEventTransform.Create<object, Older>(e => [new Legacy(e.Values), new Current(e.Values)]),
                ReadEventTransform.Create<object, Legacy>(_ => []))
            .UseIgnoredEventSentinel(IgnoredEvent.Instance);

        var events = await store.ReadStream("s").ToArrayAsync();

        events.Select(x => x.Payload).ShouldBe([IgnoredEvent.Instance, new Current("x")]);
    }

    [Test]
    public async Task Create_FuncProducesItsOwnSourceType_ThrowsNamingTheSourceType()
    {
        _inner.ReadEvents.Add(FakeEventStore.ReadEventAt(new Legacy("a"), 0));
        var store = _inner.WithReadTransforms(ReadEventTransform.Create<object, Legacy>(e => e));

        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            store.ReadStream("s").ToArrayAsync().AsTask());

        ex.Message.ShouldContain(nameof(Legacy));
        ex.Message.ShouldNotContain("DelegateReadEventTransform");
    }

    [Test]
    public void Create_SameSourceTypeAsClassBasedTransform_Throws()
    {
        Should.Throw<ArgumentException>(() => _inner.WithReadTransforms(
            ReadEventTransform.Create<object, Legacy>(e => new Current(e.Values)),
            new LegacyTransform()));
    }

    private sealed record Older(string Values);

    private sealed record Legacy(string Values);

    private sealed record Current(string Value);

    private sealed class LegacyTransform : ReadEventTransform<object, Legacy>
    {
        protected override IEnumerable<object> Apply(Legacy @event, ReadEventInfo info) => [new Current(@event.Values)];
    }
}