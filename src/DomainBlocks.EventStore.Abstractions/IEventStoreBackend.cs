namespace DomainBlocks.EventStore.Abstractions;

public interface IEventStoreBackend<TPayload>
{
    Task AppendToStreamAsync(
        string streamId,
        IEnumerable<NewEventRecord<TPayload>> events,
        ExpectedStreamVersion expectedVersion = default,
        CancellationToken cancellationToken = default);

    Task<ReadStreamResult<EventRecord<TPayload>>> ReadStreamAsync(
        string streamId,
        StreamReadDirection direction = StreamReadDirection.Forward,
        StreamPosition? fromPosition = null,
        CancellationToken cancellationToken = default);
}