﻿using DomainLib.Persistence;
using NUnit.Framework;
using System.Threading.Tasks;

namespace DomainLib.EventStore.Testing
{
    [TestFixture]
    public class AggregateRepositoryTests : EmbeddedEventStoreTest
    {
        private SnapshotScenario Scenario { get; set; }

        [Test]
        public async Task ExceptionThrownIfSnapshotIsLoadedAndNoSnapshotPresent()
        {
            await Scenario.DispatchCommandsAndSaveEvents(100);

            Assert.ThrowsAsync<SnapshotDoesNotExistException>(async () =>
            {
                await Scenario.LoadLatestStateFromSnapshot();
            });
        }

        [Test]
        public async Task SnapshotCanBeLoadedOnceSaved()
        {
            await Scenario.DispatchCommandsAndSaveEvents(100);
            await Scenario.SaveSnapshot();

            var result = await Scenario.LoadLatestStateFromSnapshot();

            Assert.That(result.AggregateState.TotalNumber, Is.EqualTo(100));
            Assert.That(result.SnapshotVersion, Is.EqualTo(99));
            Assert.That(result.EventsLoadedCount, Is.EqualTo(0));
        }

        [Test]
        public async Task AggregateStateCanBeLoadedWhenEventsHaveBeenAppendedAfterSnapshot()
        {
            await Scenario.DispatchCommandsAndSaveEvents(100);
            await Scenario.SaveSnapshot();
            await Scenario.DispatchCommandsAndSaveEvents(10);

            var result = await Scenario.LoadLatestStateFromSnapshot();

            Assert.That(result.AggregateState.TotalNumber, Is.EqualTo(110));
            Assert.That(result.SnapshotVersion, Is.EqualTo(99));
            Assert.That(result.EventsLoadedCount, Is.EqualTo(10));
        }

        [Test]
        public async Task MultipleSnapshotsCanBeSaved()
        {
            await Scenario.DispatchCommandsAndSaveEvents(100);
            await Scenario.SaveSnapshot();
            await Scenario.DispatchCommandsAndSaveEvents(10); 
            await Scenario.SaveSnapshot();

            var result = await Scenario.LoadLatestStateFromSnapshot();

            Assert.That(result.AggregateState.TotalNumber, Is.EqualTo(110));
            Assert.That(result.SnapshotVersion, Is.EqualTo(109));
            Assert.That(result.EventsLoadedCount, Is.EqualTo(0));
        }


        [SetUp]
        public void TestSetUp()
        {
            Scenario = new SnapshotScenario();
            Scenario.Initialise(this);
        }

    }
}