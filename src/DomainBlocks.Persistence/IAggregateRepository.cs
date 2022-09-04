using System;
using System.Threading.Tasks;

namespace DomainBlocks.Persistence;

public interface IAggregateRepository
{
    Task<LoadedAggregate<TAggregateState>> LoadAsync<TAggregateState>(
        string id, AggregateLoadStrategy loadStrategy = AggregateLoadStrategy.PreferSnapshot);

    Task<long> SaveAsync<TAggregateState>(
        LoadedAggregate<TAggregateState> loadedAggregate,
        Func<LoadedAggregate<TAggregateState>, bool> snapshotPredicate = null);

    Task SaveSnapshotAsync<TAggregateState>(VersionedAggregateState<TAggregateState> versionedState);
}