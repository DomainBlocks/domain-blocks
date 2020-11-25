using System;
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

        public AggregateRepository(IEventsRepository repository, 
                                   ISnapshotRepository snapshotRepository, 
                                   EventDispatcher<TEventBase> eventDispatcher, 
                                   AggregateMetadataMap metadataMap)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _snapshotRepository = snapshotRepository ?? throw new ArgumentNullException(nameof(snapshotRepository));
            _eventDispatcher = eventDispatcher ?? throw new ArgumentNullException(nameof(eventDispatcher));
            _metadataMap = metadataMap ?? throw new ArgumentNullException(nameof(metadataMap));
        }

        public async Task<LoadedAggregateState<TAggregateState>> LoadAggregate<TAggregateState>(string id,
                                                                                                   TAggregateState initialAggregateState,
                                                                                                   AggregateLoadStrategy loadStrategy = AggregateLoadStrategy.PreferSnapshot)
        {
            if (id == null) throw new ArgumentNullException(nameof(id));

            // If we choose to load solely from an event stream, then we need some initial state 
            // onto which to apply the events.
            if (initialAggregateState == null && loadStrategy == AggregateLoadStrategy.UseEventStream)
            {
                throw new ArgumentNullException(nameof(initialAggregateState));
            }

            var stateToAppendEventsTo = initialAggregateState;
            var aggregateMetadata = _metadataMap.GetForType<TAggregateState>();
            var streamName = aggregateMetadata.GetKeyFromIdentifier(id);
            long loadStartPosition = 0;
            long? snapshotVersion = null;

            if (loadStrategy == AggregateLoadStrategy.UseSnapshot ||
                loadStrategy == AggregateLoadStrategy.PreferSnapshot)
            {
                var snapshotKey = aggregateMetadata.GetSnapshotKeyFromIdentifier(id);
                var (isSuccess, snapshot) = await _snapshotRepository.TryLoadSnapshotAsync<TAggregateState>(snapshotKey);

                if (!isSuccess)
                {
                    if (loadStrategy == AggregateLoadStrategy.UseSnapshot)
                    {
                        throw new SnapshotDoesNotExistException(snapshotKey);
                    }
                }
                else
                {
                    snapshotVersion = snapshot.Version;
                    stateToAppendEventsTo = snapshot.SnapshotState;
                    loadStartPosition = snapshot.Version + 1;
                }
            }

            if (stateToAppendEventsTo == null)
            {
                throw new InvalidOperationException("Snapshot was not found, and no initial state " +
                                                    "was supplied onto which to append events");
            }

            var events = await _repository.LoadEventsAsync<TEventBase>(streamName, loadStartPosition);
            var state = _eventDispatcher.ImmutableDispatch(stateToAppendEventsTo, events);
            var newVersion = loadStartPosition + events.Count - 1;

            return new LoadedAggregateState<TAggregateState>(state, newVersion, snapshotVersion, events.Count);
        }

        public async Task<long> SaveAggregate<TAggregateState>(string id, long expectedVersion, IEnumerable<TEventBase> eventsToApply)
        {
            if (id == null) throw new ArgumentNullException(nameof(id));
            if (eventsToApply == null) throw new ArgumentNullException(nameof(eventsToApply));

            var aggregateMetadata = _metadataMap.GetForType<TAggregateState>();
            var streamName = aggregateMetadata.GetKeyFromIdentifier(id);
            return await _repository.SaveEventsAsync(streamName, expectedVersion, eventsToApply);
        }

        public async Task SaveSnapshot<TAggregateState>(VersionedAggregateState<TAggregateState> versionedState)
        {
            if (versionedState == null) throw new ArgumentNullException(nameof(versionedState));

            var aggregateMetadata = _metadataMap.GetForType<TAggregateState>();
            var snapshotStreamId = aggregateMetadata.GetSnapshotKeyFromAggregate(versionedState.AggregateState);

            await _snapshotRepository.SaveSnapshotAsync(snapshotStreamId, versionedState.Version, versionedState.AggregateState);
        }
    }
}