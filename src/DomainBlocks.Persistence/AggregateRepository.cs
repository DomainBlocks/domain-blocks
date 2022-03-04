using System;
using System.Threading.Tasks;
using DomainBlocks.Aggregates;
using DomainBlocks.Aggregates.Registration;

namespace DomainBlocks.Persistence
{
    public static class AggregateRepository
    {
        public static AggregateRepository<TCommandBase, TEventBase, TRawData> Create<TCommandBase, TEventBase, TRawData>(IEventsRepository<TRawData> repository,
                                                                                             ISnapshotRepository snapshotRepository,
                                                                                             AggregateRegistry<TCommandBase, TEventBase> aggregateRegistry)
        {
            return new AggregateRepository<TCommandBase, TEventBase, TRawData>(repository, snapshotRepository, aggregateRegistry);
        }
    }

    public sealed class AggregateRepository<TCommandBase, TEventBase, TRawData> : IAggregateRepository<TCommandBase, TEventBase>
    {
        private readonly IEventsRepository<TRawData> _eventsRepository;
        private readonly ISnapshotRepository _snapshotRepository;
        private readonly EventDispatcher<TEventBase> _eventDispatcher;
        private readonly AggregateMetadataMap _metadataMap;
        private readonly CommandDispatcher<TCommandBase, TEventBase> _commandDispatcher;

        public AggregateRepository(IEventsRepository<TRawData> eventsRepository, 
                                   ISnapshotRepository snapshotRepository,
                                   AggregateRegistry<TCommandBase, TEventBase> aggregateRegistry)
        {
            if (aggregateRegistry == null) throw new ArgumentNullException(nameof(aggregateRegistry));
            _eventsRepository = eventsRepository ?? throw new ArgumentNullException(nameof(eventsRepository));
            _snapshotRepository = snapshotRepository ?? throw new ArgumentNullException(nameof(snapshotRepository));
            _eventDispatcher = aggregateRegistry.EventDispatcher;
            _metadataMap = aggregateRegistry.AggregateMetadataMap;
            _commandDispatcher = aggregateRegistry.CommandDispatcher;
        }

        public async Task<LoadedAggregate<TAggregateState, TCommandBase, TEventBase>> LoadAggregate<TAggregateState>(string id, AggregateLoadStrategy loadStrategy = AggregateLoadStrategy.PreferSnapshot)
        {
            if (id == null) throw new ArgumentNullException(nameof(id));

            var aggregateMetadata = _metadataMap.GetForType<TAggregateState>();
            var initialAggregateState = (TAggregateState)aggregateMetadata.GetInitialState?.Invoke();

            // If we choose to load solely from an event stream, then we need some initial state 
            // onto which to apply the events.
            if (initialAggregateState == null && loadStrategy == AggregateLoadStrategy.UseEventStream)
            {
                throw new ArgumentNullException(nameof(initialAggregateState));
            }

            var stateToAppendEventsTo = initialAggregateState;
            
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

            var events = await _eventsRepository.LoadEventsAsync<TEventBase>(streamName, loadStartPosition);

            TAggregateState state;
            if (_commandDispatcher.HasImmutableRegistrations)
            {
                state = _eventDispatcher.ImmutableDispatch(stateToAppendEventsTo, events);
            }
            else
            {
                _eventDispatcher.Dispatch(stateToAppendEventsTo, events);
                state = stateToAppendEventsTo;
            }
            
            var newVersion = loadStartPosition + events.Count - 1;

            return LoadedAggregate.Create(state, id, _commandDispatcher, newVersion, snapshotVersion, events.Count);
        }

        public async Task<long> SaveAggregate<TAggregateState>(LoadedAggregate<TAggregateState, TCommandBase, TEventBase> loadedAggregate,
                                                               Func<LoadedAggregate<TAggregateState, TCommandBase, TEventBase>, bool> snapshotPredicate = null)
        {
            if (loadedAggregate == null) throw new ArgumentNullException(nameof(loadedAggregate));
            if (loadedAggregate.HasBeenSaved)
            {
                throw new ArgumentException("Aggregate has already been saved and must be loaded from persistence " +
                                            "before being saved again");
            }

            snapshotPredicate ??= x => false;
            var aggregateMetadata = _metadataMap.GetForType<TAggregateState>();
            var streamName = aggregateMetadata.GetKeyFromIdentifier(loadedAggregate.Id);
            var newVersion = await _eventsRepository.SaveEventsAsync(streamName, loadedAggregate.Version, loadedAggregate.EventsToPersist);
            loadedAggregate.HasBeenSaved = true;

            if (snapshotPredicate(loadedAggregate))
            {
                var snapshotStreamId = aggregateMetadata.GetSnapshotKeyFromAggregate(loadedAggregate.AggregateState);

                await _snapshotRepository.SaveSnapshotAsync(snapshotStreamId, loadedAggregate.Version, loadedAggregate.AggregateState);
            }

            return newVersion;
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