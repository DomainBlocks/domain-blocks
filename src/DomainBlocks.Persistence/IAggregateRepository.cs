using System;
using System.Threading.Tasks;

namespace DomainBlocks.Persistence;

public interface IAggregateRepository
{
    Task<LoadedAggregate<TAggregateState>> LoadAggregate<TAggregateState>(
        string id, AggregateLoadStrategy loadStrategy = AggregateLoadStrategy.PreferSnapshot);

    Task<long> SaveAggregate<TAggregateState>(
        LoadedAggregate<TAggregateState> loadedAggregate,
        Func<LoadedAggregate<TAggregateState>, bool> snapshotPredicate = null);

    Task SaveSnapshot<TAggregateState>(VersionedAggregateState<TAggregateState> versionedState);
}