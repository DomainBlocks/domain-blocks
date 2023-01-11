namespace DomainBlocks.Core.Persistence;

public interface IEventsRepository
{
    Task<long> SaveEventsAsync(
        string streamName,
        long expectedStreamVersion,
        IEnumerable<object> events,
        CancellationToken cancellationToken = default);
        
    IAsyncEnumerable<object> LoadEventsAsync(
        string streamName,
        long startPosition = 0,
        CancellationToken cancellationToken = default);
}