namespace DomainBlocks.EventStore.Abstractions;

public interface IEventStoreClient<TEvent> where TEvent : notnull
{
    Task AppendToStreamAsync(
        string streamId,
        IEnumerable<AppendEvent<TEvent>> events,
        AppendToStreamOptions? options = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<ReadEvent<TEvent>> ReadStreamAsync(
        string streamId,
        ReadStreamOptions? options = null,
        CancellationToken cancellationToken = default);
}