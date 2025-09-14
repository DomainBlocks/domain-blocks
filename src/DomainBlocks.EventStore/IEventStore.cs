using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore;

public interface IEventStore
{
    Task AppendToStreamAsync(
        string streamId,
        IEnumerable<object> events,
        ExpectedStreamState expectedState = default,
        CancellationToken cancellationToken = default);

    Task<ReadStreamResult<EventRecord<object>>> ReadStreamAsync(
        string streamId,
        StreamReadDirection direction = StreamReadDirection.Forward,
        StreamPosition? fromPosition = null,
        CancellationToken cancellationToken = default);
}