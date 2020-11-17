using System.Collections.Generic;
using System.Threading.Tasks;

namespace DomainLib.Persistence
{
    public interface IAggregateRepository<in TEventBase>
    {
        Task<VersionedAggregateState<TAggregateState>> LoadAggregate<TAggregateState>(string id, TAggregateState initialAggregateState = null) where TAggregateState : class;
        Task<long> SaveAggregate<TAggregateState>(string id, long expectedVersion, IEnumerable<TEventBase> eventsToApply);
        Task SaveSnapshot<TAggregateState>(VersionedAggregateState<TAggregateState> versionedState);
    }
}