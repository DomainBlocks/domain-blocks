using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Transforms;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Unit.Decoration;

/// <summary>
/// A filter is about the events that a caller is given, which with read transforms are not the events that are stored.
/// </summary>
public class EventStoreDecoratorFilterTests
{
    private FakeEventStore _inner = null!;
    private IEventStore<object, string, StreamPosition, LogPosition> _store = null!;

    [SetUp]
    public void SetUp()
    {
        _inner = new FakeEventStore();
        _store = _inner.WithReadTransforms(new SplitTransform());

        _inner.ReadEvents.Add(FakeEventStore.ReadEventAt(new Legacy("a;b"), 0, Metadata("tenant", "acme")));
        _inner.ReadEvents.Add(FakeEventStore.ReadEventAt(new Other("x"), 1, Metadata("tenant", "acme")));
        _inner.ReadEvents.Add(FakeEventStore.ReadEventAt(new Current("c"), 2, Metadata("tenant", "initech")));
        _inner.ReadEvents.Add(FakeEventStore.ReadEventAt(new Legacy("d"), 3, Metadata("tenant", "initech")));
    }

    [Test]
    public async Task ReadAll_WhenFilteredByATypeThatATransformMakes_SelectsWhatEventsTurnInto()
    {
        var read = await _store.ReadAll(options: new() { Filter = EventFilter.OfType<Current>() }).ToArrayAsync();

        read.Select(x => x.Payload).ShouldBe([new Current("a"), new Current("b"), new Current("c"), new Current("d")]);
    }

    [Test]
    public async Task ReadAll_WhenFilteredByAType_AsksTheStoreForWhatATransformAppliesToAsWell()
    {
        var filter = EventFilter.OfType<Current>();

        await _store.ReadAll(options: new() { Filter = filter }).ToArrayAsync();

        _inner.LastReadAllOptions!.Filter.ShouldBe(filter | EventFilter.OfType<Legacy>());
    }

    [Test]
    public async Task ReadAll_WhenFilteredByAPredicate_TestsItAgainstWhatEventsTurnInto()
    {
        var filter = EventFilter.OfType<Current>(e => e.Value != "a" && e.Value != "c");

        var read = await _store.ReadAll(options: new() { Filter = filter }).ToArrayAsync();

        read.Select(x => x.Payload).ShouldBe([new Current("b"), new Current("d")]);
    }

    [Test]
    public async Task ReadAll_WhenFilteredByTheTypeThatATransformAppliesTo_SelectsNothingOfIt()
    {
        // No caller is given a Legacy, so none is selected, though the store has them.
        var read = await _store.ReadAll(options: new() { Filter = EventFilter.OfType<Legacy>() }).ToArrayAsync();

        read.ShouldBeEmpty();
    }

    [Test]
    public async Task ReadAll_WhenATypeIsNegated_SelectsByWhatEventsTurnInto()
    {
        var read = await _store.ReadAll(options: new() { Filter = !EventFilter.OfType<Current>() }).ToArrayAsync();

        read.Select(x => x.Payload).ShouldBe([new Other("x")]);
    }

    [Test]
    public async Task ReadAll_WhenTypesAreCombinedWithOtherFilters_SelectsTheSameAsFilteringWhatIsReturned()
    {
        var unfiltered = await _store.ReadAll().ToArrayAsync();

        EventFilter[] filters =
        [
            EventFilter.OfType<Current>() & EventFilter.Metadata("tenant", "acme"),
            EventFilter.OfType<Other>() | EventFilter.Metadata("tenant", "initech"),
            !(EventFilter.OfType<Current>(e => e.Value == "b") | EventFilter.OfType<Other>()),
            !EventFilter.OfType<Current>() & !EventFilter.OfType<Other>(),
            EventFilter.OfType<object>(e => e.Equals(new Current("d")) || e.GetType() == typeof(Other))
        ];

        var subject = new FilterableReadEvent<object, string, StreamPosition, LogPosition>();

        foreach (var filter in filters)
        {
            var read = await _store.ReadAll(options: new() { Filter = filter }).ToArrayAsync();

            var expected = unfiltered.Where(x =>
            {
                subject.Set(x);
                return filter.Matches(subject);
            });

            read.Select(x => x.Payload).ShouldBe(expected.Select(x => x.Payload), filter.ToString());
        }
    }

    [Test]
    public void ExplainFilter_WhenFilteredByAType_ExplainsWhatTheStoreIsAsked()
    {
        var filter = EventFilter.OfType<Current>();

        _store.ExplainFilter(filter).Pushdown.ShouldBe(filter | EventFilter.OfType<Legacy>());

        // A filter that says nothing of types is left to the store as it is.
        var byTenant = EventFilter.Metadata("tenant", "acme");

        _store.ExplainFilter(byTenant, FilterPushdownMode.None).Pushdown.ShouldBe(byTenant);
        _inner.LastExplainedPushdown.ShouldBe(FilterPushdownMode.None);
    }

    [Test]
    public async Task ReadAll_WhenAnEventIsReadAsTheTypeOfATransformThatDoesNotApplyToIt_GoesByTheFilter()
    {
        // A transform applies to events of its own type, and the store is asked for every event that is read as it.
        _inner.ReadEvents.Add(FakeEventStore.ReadEventAt(new RushLegacy("e"), 4, Metadata("tenant", "acme")));

        var read = await _store.ReadAll(options: new() { Filter = EventFilter.OfType<Other>() }).ToArrayAsync();

        read.Select(x => x.Payload).ShouldBe([new Other("x")]);

        var options = new ReadAllOptions { Filter = EventFilter.OfType<Other>() | EventFilter.OfType<RushLegacy>() };

        read = await _store.ReadAll(options: options).ToArrayAsync();

        read.Select(x => x.Payload).ShouldBe([new Other("x"), new RushLegacy("e")]);
    }

    [Test]
    public async Task SubscribeToAll_WhenAnEventIsReadAsTheTypeOfATransformThatDoesNotApplyToIt_GoesByTheFilter()
    {
        _inner.ReadEvents.Add(FakeEventStore.ReadEventAt(new RushLegacy("e"), 4, Metadata("tenant", "acme")));

        foreach (var e in _inner.ReadEvents)
            _inner.SubscriptionMessages.Add(SubscriptionMessage.Event(e));

        var options = new SubscriptionOptions { Filter = EventFilter.OfType<Other>() };

        var messages = await _store.SubscribeToAll(SubscriptionOrigin.Start, options).ToArrayAsync();

        messages.Where(x => x.Event is not null).Select(x => x.Event!.Value.Payload).ShouldBe([new Other("x")]);
    }

    [Test]
    public async Task ReadAll_WhenTheFilterSaysNothingOfTypes_LeavesItToTheStore()
    {
        var options = new ReadAllOptions { Filter = EventFilter.Metadata("tenant", "initech"), MaxCount = 1 };

        var read = await _store.ReadAll(options: options).ToArrayAsync();

        _inner.LastReadAllOptions.ShouldBeSameAs(options);
        read.Select(x => x.Payload).ShouldBe([new Current("c")]);
    }

    [Test]
    public async Task ReadAll_WhenFilteredByATypeWithMaxCount_CountsTheStoredEventsThatAnythingIsReturnedOf()
    {
        var options = new ReadAllOptions { Filter = EventFilter.OfType<Current>(e => e.Value != "c"), MaxCount = 2 };

        var read = await _store.ReadAll(options: options).ToArrayAsync();

        // The first stored event gives two, the third gives none, and the fourth is the second to give any.
        read.Select(x => x.Payload).ShouldBe([new Current("a"), new Current("b"), new Current("d")]);
        _inner.LastReadAllOptions!.MaxCount.ShouldBeNull();
    }

    [Test]
    public async Task ReadAll_WhenFilteredByATypeAndMetadataWithMetadataLeftOut_SelectsByItAndLeavesItOut()
    {
        var options = new ReadAllOptions
        {
            Filter = EventFilter.OfType<Current>() & EventFilter.Metadata("tenant", "acme"),
            IncludeMetadata = false
        };

        var read = await _store.ReadAll(options: options).ToArrayAsync();

        read.Select(x => x.Payload).ShouldBe([new Current("a"), new Current("b")]);
        read.ShouldAllBe(x => x.Context.Metadata.Count == 0);
        _inner.LastReadAllOptions!.IncludeMetadata.ShouldBeTrue();
    }

    [Test]
    public async Task ReadStream_WhenFilteredByAType_SelectsWhatEventsTurnInto()
    {
        var options = new ReadStreamOptions { Filter = EventFilter.OfType<Current>(e => e.Value == "b"), MaxCount = 1 };

        var read = await _store.ReadStream("stream-1", options: options).ToArrayAsync();

        read.Select(x => x.Payload).ShouldBe([new Current("b")]);
        _inner.LastReadStreamOptions!.MaxCount.ShouldBeNull();
    }

    [Test]
    public async Task Subscriptions_WhenFilteredByAType_DeliverWhatEventsTurnIntoAndEveryOtherMessage()
    {
        foreach (var e in _inner.ReadEvents)
            _inner.SubscriptionMessages.Add(SubscriptionMessage.Event(e));

        _inner.SubscriptionMessages.Insert(
            2,
            SubscriptionMessage<object, string, StreamPosition, LogPosition>.CaughtUp);

        var options = new SubscriptionOptions { Filter = EventFilter.OfType<Current>(e => e.Value != "a") };

        var all = await _store.SubscribeToAll(options: options).ToArrayAsync();
        var stream = await _store.SubscribeToStream("stream-1", options: options).ToArrayAsync();

        foreach (var messages in (SubscriptionMessage<object, string, StreamPosition, LogPosition>[][])[all, stream])
        {
            messages.Length.ShouldBe(4);
            messages[0].Event!.Value.Payload.ShouldBe(new Current("b"));
            messages[1].IsCaughtUp.ShouldBeTrue();
            messages[2].Event!.Value.Payload.ShouldBe(new Current("c"));
            messages[3].Event!.Value.Payload.ShouldBe(new Current("d"));
        }

        _inner.LastSubscriptionOptions!.Filter.ShouldBe(options.Filter | EventFilter.OfType<Legacy>());
    }

    [Test]
    public async Task Subscriptions_WhenTheFilterLeavesNothingOfAnEvent_SayHowFarTheyHaveLookedBeforeCaughtUp()
    {
        // The store delivered the event, so it has no more to say of it, and the subscriber was given nothing.
        _inner.SubscriptionMessages.Add(SubscriptionMessage.Event(_inner.ReadEvents[0]));
        _inner.SubscriptionMessages.Add(SubscriptionMessage.Event(_inner.ReadEvents[3]));
        _inner.SubscriptionMessages.Add(SubscriptionMessage<object, string, StreamPosition, LogPosition>.CaughtUp);

        var options = new SubscriptionOptions
        {
            Filter = EventFilter.OfType<Current>(e => e.Value == "nothing"),
            CheckpointInterval = TimeSpan.FromHours(1)
        };

        var all = await _store.SubscribeToAll(options: options).ToArrayAsync();
        var stream = await _store.SubscribeToStream("stream-1", options: options).ToArrayAsync();

        // The second is kept back within the hour, until the store has something else to say.
        all.Length.ShouldBe(3);
        all[0].LogCheckpoint.Value.ShouldBe(_inner.ReadEvents[0].Context.LogPosition);
        all[1].LogCheckpoint.Value.ShouldBe(_inner.ReadEvents[3].Context.LogPosition);
        all[2].IsCaughtUp.ShouldBeTrue();

        stream.Length.ShouldBe(3);
        stream[1].StreamCheckpoint.Value.ShouldBe(_inner.ReadEvents[3].Context.StreamPosition);
    }

    [Test]
    public async Task Subscriptions_WhenTheFilterLeavesNothingOfAnEvent_SaySoAtMostOnceInAnInterval()
    {
        _inner.SubscriptionMessages.Add(SubscriptionMessage.Event(_inner.ReadEvents[0]));
        _inner.SubscriptionMessages.Add(SubscriptionMessage.Event(_inner.ReadEvents[3]));

        var options = new SubscriptionOptions
        {
            Filter = EventFilter.OfType<Current>(e => e.Value == "nothing"),
            CheckpointInterval = TimeSpan.FromHours(1)
        };

        var messages = await _store.SubscribeToAll(options: options).ToArrayAsync();

        // At once for the first, and the second is within the hour.
        messages.Select(x => x.LogCheckpoint.Value).ShouldBe([_inner.ReadEvents[0].Context.LogPosition]);
    }

    [Test]
    public async Task Subscriptions_WhenAnEventFollowsOneThatTheFilterLeftNothingOf_SayNoMoreThanTheEventDoes()
    {
        _inner.SubscriptionMessages.Add(SubscriptionMessage.Event(_inner.ReadEvents[0]));
        _inner.SubscriptionMessages.Add(SubscriptionMessage.Event(_inner.ReadEvents[2]));
        _inner.SubscriptionMessages.Add(SubscriptionMessage.Event(_inner.ReadEvents[3]));
        _inner.SubscriptionMessages.Add(SubscriptionMessage<object, string, StreamPosition, LogPosition>.CaughtUp);

        var options = new SubscriptionOptions
        {
            Filter = EventFilter.OfType<Current>(e => e.Value == "d"),
            CheckpointInterval = TimeSpan.FromHours(1)
        };

        var messages = await _store.SubscribeToAll(options: options).ToArrayAsync();

        // The second event is kept back within the hour, and the third says more than it would.
        messages.Length.ShouldBe(3);
        messages[0].LogCheckpoint.Value.ShouldBe(_inner.ReadEvents[0].Context.LogPosition);
        messages[1].Event!.Value.Payload.ShouldBe(new Current("d"));
        messages[2].IsCaughtUp.ShouldBeTrue();
    }

    private static Dictionary<string, string> Metadata(string key, string value) => new() { [key] = value };

    private record Legacy(string Values);

    private sealed record RushLegacy(string Values) : Legacy(Values);

    private sealed record Current(string Value);

    private sealed record Other(string Value);

    private sealed class SplitTransform : ReadEventTransform<object, Legacy>
    {
        protected override IEnumerable<object> Apply(Legacy @event, ReadEventInfo info) =>
            @event.Values.Split(';').Select(x => new Current(x));
    }
}