namespace DomainBlocks.EventStore.Abstractions;

public interface IEventStoreClient<in TStreamId, TEvent> where TStreamId : notnull where TEvent : notnull
{
    Task AppendToStreamAsync(
        TStreamId streamId,
        IEnumerable<AppendEvent<TEvent>> events,
        AppendToStreamOptions? options = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<ReadEvent<TEvent>> ReadAll(ReadAllOptions? options = null);

    IAsyncEnumerable<ReadEvent<TEvent>> ReadStream(TStreamId streamId, ReadStreamOptions? options = null);

    IAsyncEnumerable<SubscriptionMessage> SubscribeToAll(SubscribeToAllOptions? options = null);

    IAsyncEnumerable<SubscriptionMessage> SubscribeToStream(
        TStreamId streamId,
        SubscribeToStreamOptions? options = null);
}

public interface IEventStoreClient<TEvent> : IEventStoreClient<string, TEvent> where TEvent : notnull;