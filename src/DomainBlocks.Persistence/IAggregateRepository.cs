using System;
using System.Threading.Tasks;

namespace DomainBlocks.Persistence
{
    public interface IAggregateRepository<TCommandBase, TEventBase>
    {
        Task<LoadedAggregate<TAggregateState, TCommandBase, TEventBase>> LoadAggregate<TAggregateState>(string id,
            AggregateLoadStrategy loadStrategy = AggregateLoadStrategy.PreferSnapshot);

        Task<long> SaveAggregate<TAggregateState>(
            LoadedAggregate<TAggregateState, TCommandBase, TEventBase> loadedAggregate,
            Func<LoadedAggregate<TAggregateState, TCommandBase, TEventBase>, bool> snapshotPredicate = null);

        Task SaveSnapshot<TAggregateState>(VersionedAggregateState<TAggregateState> versionedState);
    }
}