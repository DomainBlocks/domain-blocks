using System.Collections.Generic;
using System.Threading.Tasks;

namespace DomainLib.Persistence
{
    public interface IEventsRepository
    {
        Task<long> SaveEventsAsync<TEvent>(string streamName, long expectedStreamVersion, IEnumerable<TEvent> events);
        Task<TEvent[]> LoadEventsAsync<TEvent>(string streamName, long startPosition = 0);
    }
}