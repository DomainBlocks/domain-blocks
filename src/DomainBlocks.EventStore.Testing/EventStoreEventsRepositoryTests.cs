using System;
using System.Linq;
using System.Threading.Tasks;
using DomainBlocks.Core.Serialization;
using DomainBlocks.Core.Serialization.EventStore;
using DomainBlocks.Persistence;
using DomainBlocks.Persistence.EventStore;
using DomainBlocks.Testing;
using NUnit.Framework;

namespace DomainBlocks.EventStore.Testing;

[TestFixture]
public class EventStoreEventsRepositoryTests : EventStoreIntegrationTest
{
    [TestCase(2, Description = "Small batch that can be saved in one round trip")]
    [TestCase(5000, Description = "Large batch that will need a transaction")]
    public async Task EventsCanBeSaved(int eventCount)
    {
        var repo = CreateRepository();
        var nextVersion = await repo.SaveEventsAsync(EventStoreTestHelpers.RandomStreamName(),
            StreamVersion.NewStream,
            EventStoreTestHelpers.GenerateTestEvents(eventCount));

        Assert.That(nextVersion, Is.EqualTo(eventCount - 1));
    }

    [Test]
    public void ArgumentNullExceptionIsThrownWhenEventArrayIsNullOnSave()
    {
        Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            var repo = CreateRepository();
            await repo.SaveEventsAsync(EventStoreTestHelpers.RandomStreamName(), StreamVersion.NewStream, null);
        });
    }

    [Test]
    public void ArgumentNullExceptionIsThrownWhenStreamIsNullOnSave()
    {
        Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            var repo = CreateRepository();
            await repo.SaveEventsAsync(null,
                StreamVersion.NewStream,
                EventStoreTestHelpers.GenerateTestEvents(2));
        });
    }

    [Test]
    public async Task EmptyEventArrayDoesNotCauseExceptionOnSave()
    {
        var repo = CreateRepository();
        var nextVersion = await repo.SaveEventsAsync(EventStoreTestHelpers.RandomStreamName(),
            StreamVersion.NewStream,
            new TestEvent[] { });

        Assert.That(nextVersion, Is.EqualTo(-1));
    }

    [TestCase(2, Description = "Small batch that can be read in one round trip")]
    [TestCase(5000, Description = "Large batch that will need multiple reads")]
    public async Task EventsCanBeLoaded(int eventCount)
    {
        var repo = CreateRepository();
        var streamName = EventStoreTestHelpers.RandomStreamName();
        await repo.SaveEventsAsync(streamName,
            StreamVersion.NewStream,
            EventStoreTestHelpers.GenerateTestEvents(eventCount));

        var events = await repo.LoadEventsAsync(streamName).Cast<TestEvent>().ToListAsync();

        Assert.That(events.Count, Is.EqualTo(eventCount));

        for (var i = 0; i < eventCount; i++)
        {
            Assert.That(events[i].Number, Is.EqualTo(i + 1));
        }
    }

    // [Test]
    // public async Task HandlerFunctionIsCalledIfEventCannotBeRead()
    // {
    //     var repo = CreateRepository();
    //     var streamName = EventStoreTestHelpers.RandomStreamName();
    //     var version = await repo.SaveEventsAsync(streamName, StreamVersion.NewStream,
    //         EventStoreTestHelpers.GenerateTestEvents(2));
    //
    //     var badEventDataString = "This is not JSON. It should fail on deserialize";
    //     var badEventData = new EventData(Uuid.NewUuid(), nameof(TestEvent), Encoding.UTF8.GetBytes(badEventDataString));
    //     var nextStreamRevision =
    //         (await EventStoreClient.AppendToStreamAsync(streamName,
    //             StreamRevision.FromInt64(version),
    //             EnumerableEx.Return(badEventData))).NextExpectedStreamRevision;
    //
    //     // If the bad event data is saved, the version should increment
    //     var expectedStreamRevision = StreamRevision.FromInt64(version + 1);
    //
    //     Assert.That(nextStreamRevision, Is.EqualTo(expectedStreamRevision));
    //
    //     int erroredEventCount = 0;
    //     string erroredEventData = null;
    //     Uuid? erroredEventId = null;
    //
    //     void OnEventError(IEventPersistenceData<ReadOnlyMemory<byte>> e)
    //     {
    //         erroredEventCount++;
    //         erroredEventData = Encoding.UTF8.GetString(e.EventData.Span);
    //         erroredEventId = Uuid.FromGuid(e.EventId);
    //     }
    //
    //     var loadedEvents = await repo.LoadEventsAsync(streamName, onEventError: OnEventError).ToListAsync();
    //
    //     Assert.That(loadedEvents.Count, Is.EqualTo(2)); // 2 events should load successfully
    //     Assert.That(erroredEventCount, Is.EqualTo(1));
    //     Assert.That(erroredEventId, Is.EqualTo(badEventData.EventId));
    //     Assert.That(erroredEventData, Is.EqualTo(badEventDataString));
    // }

    [Test]
    public void ArgumentNullExceptionIsThrownWhenStreamIsNullOnLoad()
    {
        Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            var repo = CreateRepository();
            await repo.LoadEventsAsync(null).ToListAsync();
        });
    }

    private IEventsRepository CreateRepository()
    {
        var serializer = new JsonBytesEventDataSerializer();
        var adapter = new EventStoreEventAdapter(serializer);
        var converter = EventConverter.Create(Fakes.EventNameMap, adapter);
        return new EventStoreEventsRepository(EventStoreClient, converter);
    }
}