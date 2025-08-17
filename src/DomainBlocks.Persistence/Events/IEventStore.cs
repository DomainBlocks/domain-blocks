using DomainBlocks.Persistence.Abstractions.Events;

namespace DomainBlocks.Persistence.Events;

public interface IEventStore
{
    Task AppendToStreamAsync(
        string streamId,
        IEnumerable<object> events,
        long? expectedVersion = null,
        CancellationToken cancellationToken = default);

    Task<ReadStreamResult<object>> ReadStreamAsync(
        string streamId,
        StreamReadDirection direction = StreamReadDirection.Forward,
        long? fromVersion = null,
        CancellationToken cancellationToken = default);
}