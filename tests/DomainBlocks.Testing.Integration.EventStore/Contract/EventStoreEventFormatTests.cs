using DomainBlocks.EventStore;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing.Events;
using DomainBlocks.Testing.Events.Proto;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.Testing.Integration.EventStore.Contract;

/// <summary>
/// Every event format the store's test codec can be created with round-trips an event. Formats the codec does not
/// support are reported as ignored.
/// </summary>
public abstract class EventStoreEventFormatTests<TStreamPos, TLogPos>(
    IEventStoreTestHarness<TStreamPos, TLogPos> harness) :
    EventStoreTestBase<TStreamPos, TLogPos>(harness)
    where TStreamPos : notnull
    where TLogPos : notnull
{
    [TestCase(EventFormat.Json)]
    [TestCase(EventFormat.Bson)]
    [TestCase(EventFormat.Protobuf)]
    [CancelAfter(TestTimeouts.DefaultMillis)]
    public async Task AppendAsync_EventInFormat_RoundTripsThroughReadStream(
        EventFormat format,
        CancellationToken cancellationToken)
    {
        if (!Harness.SupportedFormats.Contains(format))
            Assert.Ignore($"The store's test codec does not support {format}.");

        // Protobuf can only serialise generated message types; the other formats take a plain record.
        object @event = format == EventFormat.Protobuf
            ? new ProtoTestEvent { Value = "test-123" }
            : new TestEvent { Value = "test-123" };

        var eventTypeMap = EventTypeMap.Create(
            EventTypeMapping.ReadWrite<TestEvent>(),
            EventTypeMapping.ReadWrite<ProtoTestEvent>(nameof(ProtoTestEvent)));

        var eventStore = CreateEventStore(eventTypeMap, format);
        await using var disposable = eventStore as IAsyncDisposable;

        var streamId = $"test-{format}-{Guid.NewGuid():N}";

        await eventStore.AppendAsync(streamId, [@event], cancellationToken: cancellationToken);

        var readEvents = await eventStore.ReadStream(streamId).ToArrayAsync(cancellationToken);

        readEvents.ShouldHaveSingleItem().Payload.ShouldBe(@event);
    }
}