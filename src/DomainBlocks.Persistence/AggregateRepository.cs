using System;
using System.Linq;
using System.Threading.Tasks;
using DomainBlocks.Persistence.New;

namespace DomainBlocks.Persistence;

public static class AggregateRepository
{
    public static AggregateRepository<TRawData> Create<TRawData>(
        IEventsRepository<TRawData> eventsRepository, ISnapshotRepository snapshotRepository, Model model)
    {
        return new AggregateRepository<TRawData>(eventsRepository, snapshotRepository, model);
    }
}

public sealed class AggregateRepository<TRawData> : IAggregateRepository
{
    private readonly IEventsRepository<TRawData> _eventsRepository;
    private readonly ISnapshotRepository _snapshotRepository;
    private readonly Model _model;

    public AggregateRepository(
        IEventsRepository<TRawData> eventsRepository, ISnapshotRepository snapshotRepository, Model model)
    {
        _eventsRepository = eventsRepository ?? throw new ArgumentNullException(nameof(eventsRepository));
        _snapshotRepository = snapshotRepository ?? throw new ArgumentNullException(nameof(snapshotRepository));
        _model = model ?? throw new ArgumentNullException(nameof(model));
    }

    public async Task<LoadedAggregate<TAggregateState>> LoadAggregate<TAggregateState>(
        string id, AggregateLoadStrategy loadStrategy = AggregateLoadStrategy.PreferSnapshot)
    {
        if (id == null) throw new ArgumentNullException(nameof(id));

        var aggregateType = _model.GetAggregateType<TAggregateState>();
        var initialAggregateState = aggregateType.CreateNew();

        // If we choose to load solely from an event stream, then we need some initial state
        // onto which to apply the events.
        // TODO (DS): This logic seems wrong, as initial state is never null.
        if (initialAggregateState == null && loadStrategy == AggregateLoadStrategy.UseEventStream)
        {
            throw new ArgumentNullException(nameof(initialAggregateState));
        }

        var stateToAppendEventsTo = initialAggregateState;

        var streamName = aggregateType.SelectStreamKeyFromId(id);
        long loadStartPosition = 0;
        long? snapshotVersion = null;

        if (loadStrategy is AggregateLoadStrategy.UseSnapshot or AggregateLoadStrategy.PreferSnapshot)
        {
            var snapshotKey = aggregateType.SelectSnapshotKeyFromId(id);
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

        var events = await _eventsRepository.LoadEventsAsync(streamName, loadStartPosition);
        var state = events.Aggregate(stateToAppendEventsTo, aggregateType.ApplyEvent);
        var newVersion = loadStartPosition + events.Count - 1;

        return LoadedAggregate.Create(state, id, newVersion, snapshotVersion, events.Count, aggregateType);
    }

    public async Task<long> SaveAggregate<TAggregateState>(
        LoadedAggregate<TAggregateState> loadedAggregate,
        Func<LoadedAggregate<TAggregateState>, bool> snapshotPredicate = null)
    {
        if (loadedAggregate == null) throw new ArgumentNullException(nameof(loadedAggregate));
        if (loadedAggregate.HasBeenSaved)
        {
            throw new ArgumentException("Aggregate has already been saved and must be loaded from persistence " +
                                        "before being saved again");
        }

        snapshotPredicate ??= _ => false;
        var aggregateType = _model.GetAggregateType<TAggregateState>();
        var streamName = aggregateType.SelectStreamKeyFromId(loadedAggregate.Id);
        
        var newVersion = await _eventsRepository.SaveEventsAsync(
            streamName, loadedAggregate.Version, loadedAggregate.EventsToPersist);
        
        loadedAggregate.HasBeenSaved = true;

        if (snapshotPredicate(loadedAggregate))
        {
            var snapshotKey = aggregateType.SelectSnapshotKey(loadedAggregate.AggregateState);

            await _snapshotRepository.SaveSnapshotAsync(
                snapshotKey, loadedAggregate.Version, loadedAggregate.AggregateState);
        }

        return newVersion;
    }

    public async Task SaveSnapshot<TAggregateState>(VersionedAggregateState<TAggregateState> versionedState)
    {
        if (versionedState == null) throw new ArgumentNullException(nameof(versionedState));

        var aggregateType = _model.GetAggregateType<TAggregateState>();
        var snapshotStreamId = aggregateType.SelectSnapshotKey(versionedState.AggregateState);

        await _snapshotRepository.SaveSnapshotAsync(
            snapshotStreamId, versionedState.Version, versionedState.AggregateState);
    }
}