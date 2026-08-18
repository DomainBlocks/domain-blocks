namespace DomainBlocks.EventStore.Abstractions.New;

public interface IEventStoreClient2<in TAppendEvent, out TReadEvent, in TStreamId, TStreamPos, in TLogPos>
    where TAppendEvent : notnull
    where TReadEvent : notnull
    where TStreamId : notnull
    where TStreamPos : notnull
    where TLogPos : notnull
{
    Task AppendToStreamAsync(
        TStreamId streamId,
        ExpectedStreamState2<TStreamPos> expectedState,
        IEnumerable<TAppendEvent> events,
        AppendToStreamOptions? options = null,
        CancellationToken cancellationToken = default);

    IEventReadBuilder<TReadEvent, TLogPos> ReadAll();

    IEventReadBuilder<TReadEvent, TStreamPos> ReadStream(TStreamId streamId);

    IEventSubscriptionBuilder<TReadEvent, TLogPos> SubscribeToAll(SubscriptionOptions? options = null);

    IEventSubscriptionBuilder<TReadEvent, TStreamPos> SubscribeToStream(
        TStreamId streamId,
        SubscriptionOptions? options = null);
}