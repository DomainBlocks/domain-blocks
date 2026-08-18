namespace DomainBlocks.EventStore.Abstractions.New;

public interface IEventStoreClient2<in TAppendEvent, out TReadEvent, in TStreamId, TStreamPos, TLogPos>
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

    IAsyncEnumerable<TReadEvent> ReadAll(ReadDefinition<TLogPos> definition, ReadAllOptions? options = null);

    IAsyncEnumerable<TReadEvent> ReadStream(
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