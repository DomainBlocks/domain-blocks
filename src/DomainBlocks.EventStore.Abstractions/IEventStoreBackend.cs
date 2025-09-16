namespace DomainBlocks.EventStore.Abstractions;

public interface IEventStoreBackend<TPayload>
{
    Task AppendToStreamAsync(
        string streamId,
        IEnumerable<UncommittedEvent<TPayload>> events,
        ExpectedStreamState expectedState = default,
        CancellationToken cancellationToken = default);

    Task<ReadStreamResult<CommittedEvent<TPayload>>> ReadStreamAsync(
        string streamId,
        ReadStreamOptions? options = null,
        CancellationToken cancellationToken = default);
}