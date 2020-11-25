using DomainLib.Persistence;
using DomainLib.Persistence.EventStore;
using DomainLib.Serialization.Json;
using DomainLib.Testing;
using NUnit.Framework;
using System;
using System.Text;
using System.Threading.Tasks;
using DomainLib.Serialization;
using EventStore.ClientAPI;

namespace DomainLib.EventStore.Testing
{
    [TestFixture]
    public class EventStoreEventsRepositoryTests : EmbeddedEventStoreTest
    {
        [TestCase(2, Description = "Small batch that can be saved in one round trip")]
        [TestCase(5000, Description = "Large batch that will need a transaction")]
        public async Task EventsCanBeSaved(int eventCount)
        {
            var repo = CreateRepository();
            var nextVersion = await repo.SaveEventsAsync(EventStoreTestHelpers.RandomStreamName(), 
                                                         StreamVersion.NewStream, 
                                                         EventStoreTestHelpers.GenerateTestEvents(eventCount));

            Assert.That(nextVersion, Is.EqualTo(eventCount-1));
        }

        [Test]
        public void ArgumentNullExceptionIsThrownWhenEventArrayIsNullOnSave()
        {
            Assert.ThrowsAsync<ArgumentNullException>(async () =>
            {
                var repo = CreateRepository();
                await repo.SaveEventsAsync<TestEvent>(EventStoreTestHelpers.RandomStreamName(), 
                                                      StreamVersion.NewStream, 
                                                      null);
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
                                                         new TestEvent[]{});

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

            var events = await repo.LoadEventsAsync<TestEvent>(streamName);

            Assert.That(events.Count, Is.EqualTo(eventCount));

            for (var i = 0; i < eventCount; i++)
            {
                Assert.That(events[i].Number, Is.EqualTo(i+1));
            }
        }

        [Test]
        public async Task HandlerFunctionIsCalledIfEventCannotBeRead()
        {
            var repo = CreateRepository();
            var streamName = EventStoreTestHelpers.RandomStreamName();
            var version = await repo.SaveEventsAsync(streamName, StreamVersion.NewStream, EventStoreTestHelpers.GenerateTestEvents(2));

            var badEventDataString = "This is not JSON. It should fail on deserialize";
            var badEventData = new EventData(Guid.NewGuid(), nameof(TestEvent), true, Encoding.UTF8.GetBytes(badEventDataString), new byte[0]);
            var newVersion = (await EventStoreConnection.AppendToStreamAsync(streamName, version, badEventData)).NextExpectedVersion;

            Assert.That(newVersion, Is.EqualTo(version + 1)); // If the bad event data is saved, the version should increment

            int erroredEventCount = 0;
            string erroredEventData = null;
            Guid? erroredEventId = null;

            void OnEventError(IEventPersistenceData e)
            {
                erroredEventCount++;
                erroredEventData = Encoding.UTF8.GetString(e.EventData);
                erroredEventId = e.EventId;
            }

            var loadedEvents = await repo.LoadEventsAsync<TestEvent>(streamName, onEventError: OnEventError);

            Assert.That(loadedEvents.Count, Is.EqualTo(2)); // 2 events should load successfully
            Assert.That(erroredEventCount, Is.EqualTo(1));
            Assert.That(erroredEventId, Is.EqualTo(badEventData.EventId));
            Assert.That(erroredEventData, Is.EqualTo(badEventDataString));
        }

        [Test]
        public void ArgumentNullExceptionIsThrownWhenStreamIsNullOnLoad()
        {
            Assert.ThrowsAsync<ArgumentNullException>(async () =>
            {
                var repo = CreateRepository();
                await repo.LoadEventsAsync<TestEvent>(null);
            });
        }

        private IEventsRepository CreateRepository()
        {
            var serializer = new JsonEventSerializer(Fakes.EventNameMap);
            var repo = new EventStoreEventsRepository(EventStoreConnection, serializer);

            return repo;
        }
    }
}