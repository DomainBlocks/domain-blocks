using DomainBlocks.Persistence.Abstractions.Events;

namespace DomainBlocks.Persistence.Events;

public interface IEventStore<TEventBase> where TEventBase : notnull
{
    Task AppendToStreamAsync(
        string streamId,
        IEnumerable<TEventBase> events,
        long? expectedVersion = null,
        CancellationToken cancellationToken = default);

    Task<ReadStreamResult<TEventBase>> ReadStreamAsync(
        string streamId,
        StreamReadDirection direction = StreamReadDirection.Forward,
        long? fromVersion = null,
        CancellationToken cancellationToken = default);
}