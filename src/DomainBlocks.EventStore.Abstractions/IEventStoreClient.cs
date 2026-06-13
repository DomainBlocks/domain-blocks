namespace DomainBlocks.EventStore.Abstractions;

public interface IEventStoreClient<in TStreamId, TEvent> where TStreamId : notnull where TEvent : notnull
{
    Task AppendToStreamAsync(
        TStreamId streamId,
        IEnumerable<AppendEvent<TEvent>> events,
        AppendToStreamOptions? options = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<ReadEvent<TEvent>> ReadStreamAsync(
        TStreamId streamId,
        ReadStreamOptions? options = null,
        CancellationToken cancellationToken = default);
}

public interface IEventStoreClient<TEvent> : IEventStoreClient<string, TEvent> where TEvent : notnull;