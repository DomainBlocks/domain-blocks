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
    private IEventStore<object, string, TStreamPos, TLogPos> _eventStore = null!;

    [SetUp]
    public void SetUp()
    {
        var eventTypeMap = new EventTypeMapBuilder().Add<TestEvent>().Build();
        _eventStore = CreateEventStore(eventTypeMap);
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_eventStore is { } eventStore)
            await eventStore.DisposeAsync();
    }

    [Test]
    public void SubscribeToAll_WithFilterWhenStoreDoesNotFilterSubscriptions_Throws()
    {
        RequireNoCapability(StoreCapabilities.FilteredSubscriptions);

        var options = new SubscriptionOptions { Filter = EventFilter.EventNames(nameof(TestEvent)) };

        Should.Throw<EventFilterNotSupportedException>(() => _eventStore.SubscribeToAll(options: options));
    }

    [Test]
    public void SubscribeToStream_WithFilterWhenStoreDoesNotFilterSubscriptions_Throws()
    {
        RequireNoCapability(StoreCapabilities.FilteredSubscriptions);

        var options = new SubscriptionOptions { Filter = EventFilter.EventNames(nameof(TestEvent)) };

        Should.Throw<EventFilterNotSupportedException>(() =>
            _eventStore.SubscribeToStream("stream-1", options: options));
    }
}