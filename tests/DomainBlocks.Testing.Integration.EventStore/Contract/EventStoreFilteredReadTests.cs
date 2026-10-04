using DomainBlocks.EventStore;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing.Events;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.Testing.Integration.EventStore.Contract;

/// <summary>
/// Reading with an event filter. A store that does not filter reads refuses a filter rather than ignoring it.
/// </summary>
public abstract class EventStoreFilteredReadTests<TStreamPos, TLogPos>(
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
    public void ReadAll_WithFilterWhenStoreDoesNotFilterReads_Throws()
    {
        RequireNoCapability(StoreCapabilities.FilteredReads);

        var options = new ReadAllOptions { Filter = Filter };

        Should.Throw<EventFilterNotSupportedException>(() => EventStore.ReadAll(options: options));
    }

    [Test]
    public void ReadStream_WithFilterWhenStoreDoesNotFilterReads_Throws()
    {
        RequireNoCapability(StoreCapabilities.FilteredReads);

        var options = new ReadStreamOptions { Filter = Filter };

        Should.Throw<EventFilterNotSupportedException>(() => EventStore.ReadStream("stream-1", options: options));
    }

    [Test]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task ReadStream_WithAllFilter_ReadsEveryEvent(CancellationToken cancellationToken)
    {
        var streamId = $"test-{Guid.NewGuid():N}";
        var appended = new TestEvent { Value = "unfiltered" };
        await EventStore.AppendAsync(streamId, [appended], cancellationToken: cancellationToken);

        var options = new ReadStreamOptions { Filter = EventFilter.All };
        var read = await EventStore.ReadStream(streamId, options: options).ToArrayAsync(cancellationToken);

        read.ShouldHaveSingleItem().Payload.ShouldBe(appended);
    }
}