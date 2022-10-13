using System;
using System.Threading;
using System.Threading.Tasks;

namespace DomainBlocks.Persistence;

public interface IAggregateRepository
{
    Task<LoadedAggregate<TAggregateState>> LoadAsync<TAggregateState>(
        string id,
        AggregateLoadStrategy loadStrategy = AggregateLoadStrategy.PreferSnapshot,
        CancellationToken cancellationToken = default);

    Task<long> SaveAsync<TAggregateState>(
        LoadedAggregate<TAggregateState> loadedAggregate,
        Func<LoadedAggregate<TAggregateState>, bool> snapshotPredicate = null,
        CancellationToken cancellationToken = default);

    Task SaveSnapshotAsync<TAggregateState>(
        VersionedAggregateState<TAggregateState> versionedState,
        CancellationToken cancellationToken = default);
}