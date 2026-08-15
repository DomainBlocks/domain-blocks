namespace DomainBlocks.EventStore.Abstractions;

public interface IEventStoreClient2<TEvent, TStreamId, TStreamPos, TLogPos>
    where TEvent : notnull
    where TStreamId : notnull
    where TStreamPos : notnull
    where TLogPos : notnull
{
    Task AppendToStreamAsync(
        TStreamId streamId,
        ExpectedStreamState2<TStreamPos> expectedStreamState,
        IEnumerable<AppendEvent<TEvent>> events,
        AppendToStreamOptions? options = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<ReadEvent2<TEvent, TStreamId, TStreamPos, TLogPos>> ReadAll(
        ReadMode<TLogPos>? mode = null,
        ReadAllOptions? options = null);

    IAsyncEnumerable<ReadEvent2<TEvent, TStreamId, TStreamPos, TLogPos>> ReadStream(
        TStreamId streamId,
        ReadMode<TStreamPos>? mode = null,
        ReadStreamOptions? options = null);

    IAsyncEnumerable<SubscriptionMessage2<TEvent, TStreamId, TStreamPos, TLogPos>> SubscribeToAll(
        SubscribeOrigin<TLogPos>? origin = null,
        SubscribeToAllOptions? options = null);

    IAsyncEnumerable<SubscriptionMessage2<TEvent, TStreamId, TStreamPos, TLogPos>> SubscribeToStream(
        TStreamId streamId,
        SubscribeOrigin<TStreamPos>? origin = null,
        SubscribeToStreamOptions? options = null);
}