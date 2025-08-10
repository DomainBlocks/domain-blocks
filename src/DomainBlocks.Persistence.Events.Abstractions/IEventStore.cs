namespace DomainBlocks.Persistence.Events.Abstractions;

public interface IEventStore<TPayload>
{
    Task AppendToStreamAsync(
        string streamId,
        IEnumerable<EventData<TPayload>> events,
        long? expectedVersion = null,
        CancellationToken cancellationToken = default);

    Task<ReadStreamResult<TPayload>> ReadStreamAsync(
        string streamName,
        StreamReadDirection direction,
        long? fromVersion = null,
        CancellationToken cancellationToken = default);
}