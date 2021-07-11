using DomainLib.Aggregates;
using DomainLib.Aggregates.Registration;
using DomainLib.Persistence;
using DomainLib.Persistence.EventStore;
using DomainLib.Serialization.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DomainLib.EventStore.Testing
{
    public class SnapshotScenario
    {
        private readonly TestAggregateState _initialState;
        private EventStoreIntegrationTest _test;
        private CommandDispatcher<TestCommand, TestEvent> _commandDispatcher;
        private AggregateRepository<TestEvent, ReadOnlyMemory<byte>> _aggregateRepository;
        private Guid _id;
        private long _aggregateVersion = StreamVersion.NewStream;
        public TestAggregateState AggregateState { get; private set; }

        public SnapshotScenario()
        {
            _id = Guid.NewGuid();
            _initialState = new TestAggregateState(_id, 0);
            AggregateState = _initialState;
        }

        public void Initialise(EventStoreIntegrationTest test)
        {
            _test = test;
            SetupRepositories();
        }

        public async Task DispatchCommandsAndSaveEvents(int commandCount)
        {
            var (newState, newEventsList) = DispatchCommandsToState(AggregateState, commandCount);
            _aggregateVersion = await _aggregateRepository.SaveAggregate<TestAggregateState>(_id.ToString(), _aggregateVersion, newEventsList);
            AggregateState = newState;
        }

        public async Task SaveSnapshot()
        {
            await _aggregateRepository.SaveSnapshot(VersionedAggregateState.Create(AggregateState, _aggregateVersion));
        }

        public async Task<LoadedAggregateState<TestAggregateState>> LoadLatestStateFromEvents()
        {
            return await LoadLatestState(AggregateLoadStrategy.UseEventStream);
        }

        public async Task<LoadedAggregateState<TestAggregateState>> LoadLatestStateFromSnapshot()
        {
            return await LoadLatestState(AggregateLoadStrategy.UseSnapshot);
        }

        public async Task<LoadedAggregateState<TestAggregateState>> LoadLatestStateFromSnapshotIfAvailable()
        {
            return await LoadLatestState(AggregateLoadStrategy.PreferSnapshot);
        }

        private async Task<LoadedAggregateState<TestAggregateState>> LoadLatestState(AggregateLoadStrategy loadStrategy)
        {
            var state = await _aggregateRepository.LoadAggregate(_id.ToString(), _initialState, loadStrategy);
            _aggregateVersion = state.Version;
            AggregateState = state.AggregateState;

            return state;
        }

        private (TestAggregateState state, IList<TestEvent> events) DispatchCommandsToState(TestAggregateState initialState, int commandCount)
        {
            var state = initialState;
            var commands = Enumerable.Range(1, commandCount).Select(_ => new TestCommand(1));
            var eventsList = new List<TestEvent>();

            foreach (var command in commands)
            {
                var (newState, appliedEvents) = _commandDispatcher.ImmutableDispatch(state, command);

                eventsList.AddRange(appliedEvents);
                state = newState;
            }

            return (state, eventsList);
        }

        private void SetupRepositories()
        {
            var registryBuilder = AggregateRegistryBuilder.Create<TestCommand, TestEvent>();
            TestAggregateFunctions.Register(registryBuilder);

            var registry = registryBuilder.Build();

            var serializer = new JsonBytesEventSerializer(registry.EventNameMap);

            _commandDispatcher = registry.CommandDispatcher;
            var snapshotRepository = new EventStoreSnapshotRepository(_test.EventStoreClient, serializer);
            var eventsRepository = new EventStoreEventsRepository(_test.EventStoreClient, serializer);

            _aggregateRepository = AggregateRepository.Create(eventsRepository, snapshotRepository, registry.EventDispatcher, registry.AggregateMetadataMap);
        }
    }
}