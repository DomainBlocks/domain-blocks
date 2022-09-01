using System;
using System.Linq;
using System.Threading.Tasks;
using DomainBlocks.Persistence;
using DomainBlocks.Persistence.EventStore;
using DomainBlocks.Persistence.New.Builders;
using DomainBlocks.Serialization.Json;

namespace DomainBlocks.EventStore.Testing
{
    public class SnapshotScenario
    {
        private EventStoreIntegrationTest _test;
        private AggregateRepository<ReadOnlyMemory<byte>> _aggregateRepository;
        private LoadedAggregate<TestAggregateState, object> _loadedAggregate;
        private Guid _id;
        private long _aggregateVersion = StreamVersion.NewStream;

        public SnapshotScenario()
        {
            _id = Guid.NewGuid();
        }

        public TestAggregateState AggregateState { get; private set; }

        public async Task Initialise(EventStoreIntegrationTest test)
        {
            _test = test;
            SetupRepositories();
            _loadedAggregate = await LoadLatestStateFromEvents();
        }

        public async Task DispatchCommandsAndSaveEvents(int commandCount)
        {
            DispatchCommandsToState(commandCount);
            _aggregateVersion = await _aggregateRepository.SaveAggregate(_loadedAggregate);
            var loadedAggregate = await LoadLatestStateFromEvents();
            _loadedAggregate = loadedAggregate;
            AggregateState = _loadedAggregate.AggregateState;
        }

        public async Task SaveSnapshot()
        {
            await _aggregateRepository.SaveSnapshot<TestAggregateState, TestEvent>(
                VersionedAggregateState.Create(AggregateState, _aggregateVersion));
        }

        public async Task<LoadedAggregate<TestAggregateState, object>> LoadLatestStateFromEvents()
        {
            return await LoadLatestState(AggregateLoadStrategy.UseEventStream);
        }

        public async Task<LoadedAggregate<TestAggregateState, object>> LoadLatestStateFromSnapshot()
        {
            return await LoadLatestState(AggregateLoadStrategy.UseSnapshot);
        }

        public async Task<LoadedAggregate<TestAggregateState, object>> LoadLatestStateFromSnapshotIfAvailable()
        {
            return await LoadLatestState(AggregateLoadStrategy.PreferSnapshot);
        }

        private async Task<LoadedAggregate<TestAggregateState, object>> LoadLatestState(
            AggregateLoadStrategy loadStrategy)
        {
            var loadedAggregate =
                await _aggregateRepository.LoadAggregate<TestAggregateState, object>(_id.ToString(), loadStrategy);

            _aggregateVersion = loadedAggregate.Version;
            AggregateState = loadedAggregate.AggregateState;

            return loadedAggregate;
        }

        private void DispatchCommandsToState(int commandCount)
        {
            var commands = Enumerable.Range(1, commandCount).Select(_ => new TestCommand(1));

            foreach (var command in commands)
            {
                _loadedAggregate.ExecuteCommand(_ => TestAggregateFunctions.Execute(command));
            }
        }

        private void SetupRepositories()
        {
            var modelBuilder = new ModelBuilder();
            TestAggregateFunctions.BuildModel(modelBuilder, _id);

            var model = modelBuilder.Build();

            var serializer = new JsonBytesEventSerializer(model.EventNameMap);

            var snapshotRepository = new EventStoreSnapshotRepository(_test.EventStoreClient, serializer);
            var eventsRepository = new EventStoreEventsRepository(_test.EventStoreClient, serializer);

            _aggregateRepository = AggregateRepository.Create(eventsRepository, snapshotRepository, model);
        }
    }
}