using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DomainBlocks.Core;

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

    public async Task<LoadedAggregate<TAggregateState>> LoadAsync<TAggregateState>(
        string id,
        AggregateLoadStrategy loadStrategy = AggregateLoadStrategy.PreferSnapshot, 
        CancellationToken cancellationToken = default)
    {
        if (id == null) throw new ArgumentNullException(nameof(id));

        var aggregateOptions = _model.GetAggregateOptions<TAggregateState>();
        var initialState = aggregateOptions.CreateNew();

        // If we choose to load solely from an event stream, then we need some initial state
        // onto which to apply the events.
        // TODO (DS): This logic seems wrong, as initial state is never null.
        if (initialState == null && loadStrategy == AggregateLoadStrategy.UseEventStream)
        {
            throw new ArgumentNullException(nameof(initialState));
        }

        var stateToAppendEventsTo = initialState;

        var streamName = aggregateOptions.MakeStreamKey(id);
        long loadStartPosition = 0;
        long? snapshotVersion = null;

        if (loadStrategy is AggregateLoadStrategy.UseSnapshot or AggregateLoadStrategy.PreferSnapshot)
        {
            var snapshotKey = aggregateOptions.MakeSnapshotKey(id);
            var (isSuccess, snapshot) =
                await _snapshotRepository.TryLoadSnapshotAsync<TAggregateState>(snapshotKey, cancellationToken);

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

        var events = 
            _eventsRepository.LoadEventsAsync(streamName, loadStartPosition, cancellationToken: cancellationToken);

        var result = await events.AggregateAsync(
            new
            {
                State = stateToAppendEventsTo,
                EventCount = 0
            },
            (acc, next) => new
            {
                State = aggregateOptions.ApplyEvent(acc.State, next),
                EventCount = acc.EventCount + 1
            }, 
            cancellationToken);

        var newVersion = loadStartPosition + result.EventCount - 1;

        return LoadedAggregate.Create(result.State, aggregateOptions, id, newVersion, snapshotVersion, result.EventCount);
    }

    public async Task<long> SaveAsync<TAggregateState>(
        LoadedAggregate<TAggregateState> loadedAggregate,
        Func<LoadedAggregate<TAggregateState>, bool> snapshotPredicate = null,
        CancellationToken cancellationToken = default)
    {
        if (loadedAggregate == null) throw new ArgumentNullException(nameof(loadedAggregate));
        if (loadedAggregate.HasBeenSaved)
        {
            throw new ArgumentException("Aggregate has already been saved and must be loaded from persistence " +
                                        "before being saved again");
        }

        snapshotPredicate ??= _ => false;
        var aggregateType = _model.GetAggregateOptions<TAggregateState>();
        var streamName = aggregateType.MakeStreamKey(loadedAggregate.Id);
        
        var newVersion = await _eventsRepository.SaveEventsAsync(
            streamName, loadedAggregate.Version, loadedAggregate.EventsToPersist, cancellationToken);
        
        loadedAggregate.HasBeenSaved = true;

        if (snapshotPredicate(loadedAggregate))
        {
            var snapshotKey = aggregateType.MakeSnapshotKey(loadedAggregate.Id);

            await _snapshotRepository.SaveSnapshotAsync(
                snapshotKey, loadedAggregate.Version, loadedAggregate.State, cancellationToken: cancellationToken);
        }

        return newVersion;
    }

    public async Task SaveSnapshotAsync<TAggregateState>(
        VersionedAggregateState<TAggregateState> versionedState,
        CancellationToken cancellationToken = default)
    {
        if (versionedState == null) throw new ArgumentNullException(nameof(versionedState));

        var aggregateType = _model.GetAggregateOptions<TAggregateState>();
        var snapshotStreamId = aggregateType.MakeSnapshotKey(versionedState.AggregateState);

        await _snapshotRepository.SaveSnapshotAsync(
            snapshotStreamId,
            versionedState.Version,
            versionedState.AggregateState,
            cancellationToken: cancellationToken);
    }
}