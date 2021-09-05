using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DomainBlocks.Serialization;

namespace DomainBlocks.Persistence
{
    public interface IEventsRepository<out TRawData>
    {
        Task<long> SaveEventsAsync<TEvent>(string streamName, long expectedStreamVersion, IEnumerable<TEvent> events);
        Task<IList<TEvent>> LoadEventsAsync<TEvent>(string streamName, long startPosition = 0, Action<IEventPersistenceData<TRawData>> onEventError = null);
    }
}