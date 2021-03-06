using System;
using System.Threading.Tasks;
using DomainBlocks.Aggregates;

namespace DomainBlocks.Persistence;

public static class AggregateRepository
{
    public static AggregateRepository<TEventBase, TRawData> Create<TEventBase, TRawData>(
        IEventsRepository<TRawData> eventsRepository,
        ISnapshotRepository snapshotRepository,
        AggregateRegistry< TEventBase> aggregateRegistry)
    {
        return new AggregateRepository<TEventBase, TRawData>(eventsRepository, snapshotRepository, aggregateRegistry);
    }
}

public sealed class AggregateRepository<TEventBase, TRawData> : IAggregateRepository<TEventBase>
{
    private readonly IEventsRepository<TRawData> _eventsRepository;
    private readonly ISnapshotRepository _snapshotRepository;
    private readonly IAggregateEventRouter<TEventBase> _eventRouter;
    private readonly AggregateMetadataMap _metadataMap;

    public AggregateRepository(
        IEventsRepository<TRawData> eventsRepository,
        ISnapshotRepository snapshotRepository,
        AggregateRegistry<TEventBase> aggregateRegistry)
    {
        if (aggregateRegistry == null) throw new ArgumentNullException(nameof(aggregateRegistry));
        _eventsRepository = eventsRepository ?? throw new ArgumentNullException(nameof(eventsRepository));
        _snapshotRepository = snapshotRepository ?? throw new ArgumentNullException(nameof(snapshotRepository));
        _eventRouter = aggregateRegistry.EventRouter;
        _metadataMap = aggregateRegistry.AggregateMetadataMap;
    }

    public async Task<LoadedAggregate<TAggregateState, TEventBase>> LoadAggregate<TAggregateState>(
        string id, AggregateLoadStrategy loadStrategy = AggregateLoadStrategy.PreferSnapshot)
    {
        if (id == null) throw new ArgumentNullException(nameof(id));

        var aggregateMetadata = _metadataMap.GetForType<TAggregateState>();

        var trackingEventRouter = new TrackingAggregateEventRouter<TEventBase>(_eventRouter);

        var initialAggregateState = (TAggregateState)aggregateMetadata.InitialStateFactory?.Invoke(trackingEventRouter);

        // If we choose to load solely from an event stream, then we need some initial state
        // onto which to apply the events.
        if (initialAggregateState == null && loadStrategy == AggregateLoadStrategy.UseEventStream)
        {
            throw new ArgumentNullException(nameof(initialAggregateState));
        }

        var stateToAppendEventsTo = initialAggregateState;

        var streamName = aggregateMetadata.IdToKeySelector(id);
        long loadStartPosition = 0;
        long? snapshotVersion = null;

        if (loadStrategy is AggregateLoadStrategy.UseSnapshot or AggregateLoadStrategy.PreferSnapshot)
        {
            var snapshotKey = aggregateMetadata.IdToSnapshotKeySelector(id);
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
        var state = _eventRouter.Send(stateToAppendEventsTo, events);
        var newVersion = loadStartPosition + events.Count - 1;

        return LoadedAggregate.Create(state, id, newVersion, snapshotVersion, events.Count, trackingEventRouter);
    }

    public async Task<long> SaveAggregate<TAggregateState>(
        LoadedAggregate<TAggregateState, TEventBase> loadedAggregate,
        Func<LoadedAggregate<TAggregateState, TEventBase>, bool> snapshotPredicate = null)
    {
        if (loadedAggregate == null) throw new ArgumentNullException(nameof(loadedAggregate));
        if (loadedAggregate.HasBeenSaved)
        {
            throw new ArgumentException("Aggregate has already been saved and must be loaded from persistence " +
                                        "before being saved again");
        }

        snapshotPredicate ??= _ => false;
        var aggregateMetadata = _metadataMap.GetForType<TAggregateState>();
        var streamName = aggregateMetadata.IdToKeySelector(loadedAggregate.Id);
        var newVersion = await _eventsRepository.SaveEventsAsync(
            streamName, loadedAggregate.Version, loadedAggregate.EventsToPersist);
        loadedAggregate.HasBeenSaved = true;

        if (snapshotPredicate(loadedAggregate))
        {
            var snapshotKey = aggregateMetadata.SnapshotKeySelector(loadedAggregate.AggregateState);

            await _snapshotRepository.SaveSnapshotAsync(
                snapshotKey, loadedAggregate.Version, loadedAggregate.AggregateState);
        }

        return newVersion;
    }

    public async Task SaveSnapshot<TAggregateState>(VersionedAggregateState<TAggregateState> versionedState)
    {
        if (versionedState == null) throw new ArgumentNullException(nameof(versionedState));

        var aggregateMetadata = _metadataMap.GetForType<TAggregateState>();
        var snapshotStreamId = aggregateMetadata.SnapshotKeySelector(versionedState.AggregateState);

        await _snapshotRepository.SaveSnapshotAsync(
            snapshotStreamId, versionedState.Version, versionedState.AggregateState);
    }
}