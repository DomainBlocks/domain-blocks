namespace DomainBlocks.Persistence.Abstractions.Events;

// This is the "raw" version of an event store. IEventStore will expose CLR-typed objects. Previously, we coupled type
// mapping to the "entity store", which doesn't separate concerns, and also means that event mapping can't be reused for
// both write-side and read-side concerns.
public interface IEventDataStore<TPayload>
{
    Task AppendToStreamAsync(
        string streamId,
        IEnumerable<EventData<TPayload>> events,
        long? expectedVersion = null,
        CancellationToken cancellationToken = default);

    Task<ReadStreamResult<StoredEventData<TPayload>>> ReadStreamAsync(
        string streamId,
        StreamReadDirection direction = StreamReadDirection.Forward,
        long? fromVersion = null,
        CancellationToken cancellationToken = default);
}