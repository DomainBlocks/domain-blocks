using System;
using System.Threading.Tasks;

namespace DomainBlocks.Persistence;

public interface IAggregateRepository<TEventBase>
{
    Task<LoadedAggregate<TAggregateState, TEventBase>> LoadAggregate<TAggregateState>(
        string id,
        AggregateLoadStrategy loadStrategy = AggregateLoadStrategy.PreferSnapshot);

    Task<long> SaveAggregate<TAggregateState>(
        LoadedAggregate<TAggregateState, TEventBase> loadedAggregate,
        Func<LoadedAggregate<TAggregateState, TEventBase>, bool> snapshotPredicate = null);

    Task SaveSnapshot<TAggregateState>(VersionedAggregateState<TAggregateState> versionedState);
}