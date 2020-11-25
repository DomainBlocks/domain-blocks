using System.Collections.Generic;
using System.Threading.Tasks;

namespace DomainLib.Persistence
{
    public interface IAggregateRepository<in TEventBase>
    {
        Task<LoadedAggregateState<TAggregateState>> LoadAggregate<TAggregateState>(string id,
                                                                                   TAggregateState initialAggregateState,
                                                                                   AggregateLoadStrategy loadStrategy = AggregateLoadStrategy.PreferSnapshot);
        Task<long> SaveAggregate<TAggregateState>(string id, long expectedVersion, IEnumerable<TEventBase> eventsToApply);
        Task SaveSnapshot<TAggregateState>(VersionedAggregateState<TAggregateState> versionedState);


    }
}