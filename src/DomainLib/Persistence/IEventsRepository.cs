using System.Collections.Generic;
using System.Threading.Tasks;

namespace DomainLib.Persistence
{
    public interface IEventsRepository
    {
        Task<long> SaveEventsAsync<TEvent>(string streamName, long expectedStreamVersion, IEnumerable<TEvent> events);
        Task<IEnumerable<TEvent>> LoadEventsAsync<TEvent>(string streamName);
    }
}