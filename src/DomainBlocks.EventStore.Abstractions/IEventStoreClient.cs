namespace DomainBlocks.EventStore.Abstractions;

public interface IEventStoreClient<TEvent, TStreamId, TStreamPos, TLogPos>
    where TEvent : notnull
    where TStreamId : notnull
    where TStreamPos : notnull
    where TLogPos : notnull
{
    Task AppendToStreamAsync(
        TStreamId streamId,
        ExpectedStreamState<TStreamPos> expectedState,
        IEnumerable<AppendEvent<TEvent>> events,
        Guid? commitId = null,
        AppendToStreamOptions? options = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>> ReadAll(
        ReadDefinition<TLogPos> definition,
        ReadAllOptions? options = null);

    IAsyncEnumerable<ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>> ReadStream(
        TStreamId streamId,
        ReadDefinition<TStreamPos> definition,
        ReadStreamOptions? options = null);

    IAsyncEnumerable<SubscriptionMessage> SubscribeToAll(
        SubscriptionDefinition<TLogPos> definition,
        SubscriptionOptions? options = null);

    IAsyncEnumerable<SubscriptionMessage> SubscribeToStream(
        TStreamId streamId,
        SubscriptionDefinition<TStreamPos> definition,
        SubscriptionOptions? options = null);
}