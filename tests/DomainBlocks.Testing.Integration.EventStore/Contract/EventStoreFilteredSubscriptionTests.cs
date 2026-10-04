using DomainBlocks.EventStore;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing.Events;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.Testing.Integration.EventStore.Contract;

/// <summary>
/// Subscribing with an event filter. A store that does not filter subscriptions refuses a filter rather than ignoring
/// it.
/// </summary>
public abstract class EventStoreFilteredSubscriptionTests<TStreamPos, TLogPos>(
    IEventStoreTestHarness<TStreamPos, TLogPos> harness) :
    EventStoreTestBase<TStreamPos, TLogPos>(harness)
    where TStreamPos : notnull
    where TLogPos : notnull
{
    private static readonly EventFilter Filter = EventFilter.EventNames(nameof(TestEvent));

    private IEventStore<object, string, TStreamPos, TLogPos> EventStore { get; set; } = null!;

    [SetUp]
    public void SetUp()
    {
        var eventTypeMap = new EventTypeMapBuilder().Add<TestEvent>().Build();
        EventStore = CreateEventStore(eventTypeMap);
    }

    [TearDown]
    public async Task TearDown()
    {
        if (EventStore is { } eventStore)
            await eventStore.DisposeAsync();
    }

    [Test]
    public void SubscribeToAll_WithFilterWhenStoreDoesNotFilterSubscriptions_Throws()
    {
        RequireNoCapability(StoreCapabilities.FilteredSubscriptions);

        var options = new SubscriptionOptions { Filter = Filter };

        Should.Throw<EventFilterNotSupportedException>(() => EventStore.SubscribeToAll(options: options));
    }

    [Test]
    public void SubscribeToStream_WithFilterWhenStoreDoesNotFilterSubscriptions_Throws()
    {
        RequireNoCapability(StoreCapabilities.FilteredSubscriptions);

        var options = new SubscriptionOptions { Filter = Filter };

        Should.Throw<EventFilterNotSupportedException>(
            () => EventStore.SubscribeToStream("stream-1", options: options));
    }
}