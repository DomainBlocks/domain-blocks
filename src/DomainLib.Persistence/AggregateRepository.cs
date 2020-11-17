using System.Collections.Generic;
using System.Threading.Tasks;
using DomainLib.Aggregates;

namespace DomainLib.Persistence
{
    public sealed class AggregateRepository<TEventBase> : IAggregateRepository<TEventBase>
    {
        private readonly IEventsRepository _repository;
        private readonly ISnapshotRepository _snapshotRepository;
        private readonly EventDispatcher<TEventBase> _eventDispatcher;
        private readonly AggregateMetadataMap _metadataMap;

        public AggregateRepository(IEventsRepository repository, ISnapshotRepository snapshotRepository, EventDispatcher<TEventBase> eventDispatcher, AggregateMetadataMap metadataMap)
        {
            _repository = repository;
            _snapshotRepository = snapshotRepository;
            _eventDispatcher = eventDispatcher;
            _metadataMap = metadataMap;
        }

        public async Task<VersionedAggregateState<TAggregateState>> LoadAggregate<TAggregateState>(string id, TAggregateState initialAggregateState = null) where TAggregateState : class
        {
            var aggregateMetadata = _metadataMap.GetForType<TAggregateState>();
            var streamName = aggregateMetadata.GetKeyFromIdentifier(id);
            long loadStartPosition = 0;

            if (initialAggregateState == null)
            {
                var snapshotStreamId = aggregateMetadata.GetSnapshotKeyFromIdentifier(id);
                var snapshot = await _snapshotRepository.LoadSnapshot<TAggregateState>(snapshotStreamId);
                loadStartPosition = snapshot.Version + 1;
                initialAggregateState = snapshot.SnapshotState;
            }

            var events = await _repository.LoadEventsAsync<TEventBase>(streamName, loadStartPosition);
            var state = _eventDispatcher.DispatchEvents(initialAggregateState, events);

            // Double check this for off-by-one errors
            var newVersion = loadStartPosition + events.Length - 1;
            return new VersionedAggregateState<TAggregateState>(state, newVersion);
        }

        public async Task<long> SaveAggregate<TAggregateState>(string id, long expectedVersion, IEnumerable<TEventBase> eventsToApply)
        {
            var aggregateMetadata = _metadataMap.GetForType<TAggregateState>();
            var streamName = aggregateMetadata.GetKeyFromIdentifier(id);
            return await _repository.SaveEventsAsync(streamName, expectedVersion, eventsToApply);
        }

        public async Task SaveSnapshot<TAggregateState>(VersionedAggregateState<TAggregateState> versionedState)
        {
            var aggregateMetadata = _metadataMap.GetForType<TAggregateState>();
            var snapshotStreamId = aggregateMetadata.GetSnapshotKeyFromAggregate(versionedState.AggregateState);

            await _snapshotRepository.SaveSnapshot(snapshotStreamId, versionedState.Version, versionedState.AggregateState);
        }
    }
}