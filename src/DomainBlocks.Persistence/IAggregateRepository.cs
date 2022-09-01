using System;
using System.Threading.Tasks;

namespace DomainBlocks.Persistence;

public interface IAggregateRepository
{
    // TODO (DS): Type erase TEventBase.
    Task<LoadedAggregate<TAggregateState, TEventBase>> LoadAggregate<TAggregateState, TEventBase>(
        string id,
        AggregateLoadStrategy loadStrategy = AggregateLoadStrategy.PreferSnapshot);

    Task<long> SaveAggregate<TAggregateState, TEventBase>(
        LoadedAggregate<TAggregateState, TEventBase> loadedAggregate,
        Func<LoadedAggregate<TAggregateState, TEventBase>, bool> snapshotPredicate = null);

    Task SaveSnapshot<TAggregateState, TEventBase>(VersionedAggregateState<TAggregateState> versionedState);
}