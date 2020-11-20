using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DomainLib.Serialization;

namespace DomainLib.Persistence
{
    public interface IEventsRepository
    {
        Task<long> SaveEventsAsync<TEvent>(string streamName, long expectedStreamVersion, IEnumerable<TEvent> events);
        Task<IList<TEvent>> LoadEventsAsync<TEvent>(string streamName, long startPosition = 0, Action<IEventPersistenceData> onEventError = null);
    }
}