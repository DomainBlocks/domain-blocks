using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DomainBlocks.Serialization;

namespace DomainBlocks.Persistence;

public interface IEventsRepository<out TRawData>
{
    Task<long> SaveEventsAsync(
        string streamName,
        long expectedStreamVersion,
        IEnumerable<object> events,
        CancellationToken cancellationToken = default);
        
    IAsyncEnumerable<object> LoadEventsAsync(
        string streamName,
        long startPosition = 0,
        Action<IEventPersistenceData<TRawData>> onEventError = null,
        CancellationToken cancellationToken = default);
}
