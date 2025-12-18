using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore;

public interface IEventStoreClient
{
    Task AppendToStreamAsync(
        string streamId,
        IEnumerable<object> events,
        ExpectedStreamState expectedState = default,
        CancellationToken cancellationToken = default);

    Task AppendToStreamAsync(
        string streamId,
        IEnumerable<UncommittedEvent<object>> events,
        ExpectedStreamState expectedState = default,
        CancellationToken cancellationToken = default);

    Task<ReadStreamResult<CommittedEvent<object>>> ReadStreamAsync(
        string streamId,
        ReadStreamOptions? options = null,
        CancellationToken cancellationToken = default);
}