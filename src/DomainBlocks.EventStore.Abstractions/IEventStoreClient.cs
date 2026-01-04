namespace DomainBlocks.EventStore.Abstractions;

public interface IEventStoreClient<TEventBase> where TEventBase : class
{
    Task AppendToStreamAsync(
        string streamId,
        IEnumerable<AppendEvent<TEventBase>> events,
        AppendToStreamOptions? options = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<ReadEvent<TEventBase>> ReadStreamAsync(
        string streamId,
        ReadStreamOptions? options = null,
        CancellationToken cancellationToken = default);
}