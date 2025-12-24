namespace DomainBlocks.EventStore.Abstractions;

public interface IEventStoreAdapter<TPayload> : IAsyncDisposable where TPayload : notnull
{
    Task AppendToStreamAsync(
        string streamId,
        IEnumerable<UncommittedEvent<TPayload>> events,
        AppendToStreamOptions? options = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<CommittedEvent<TPayload>> ReadStreamAsync(
        string streamId,
        ReadStreamOptions? options = null,
        CancellationToken cancellationToken = default);
}