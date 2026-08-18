namespace DomainBlocks.EventStore.Abstractions.New;

public sealed class EventStoreClient2<TAppendEvent, TReadEvent, TStreamId, TStreamPos, TLogPos>(
    IEventStreamAppender<TAppendEvent, TStreamId, TStreamPos> appender,
    IEventReader<TReadEvent, TStreamId, TStreamPos, TLogPos> reader,
    IEventSubscriber<TStreamId, TStreamPos, TLogPos> subscriber) :
    IEventStoreClient2<TAppendEvent, TReadEvent, TStreamId, TStreamPos, TLogPos>
    where TAppendEvent : notnull
    where TReadEvent : notnull
    where TStreamId : notnull
    where TStreamPos : notnull
    where TLogPos : notnull
{
    public Task AppendToStreamAsync(
        TStreamId streamId,
        ExpectedStreamState2<TStreamPos> expectedState,
        IEnumerable<TAppendEvent> events,
        AppendToStreamOptions? options = null,
        CancellationToken cancellationToken = default) =>
        appender.AppendAsync(streamId, expectedState, events, options, cancellationToken);

    public IEventReadBuilder<TReadEvent, TLogPos> ReadAll()
    {
        return new EventReadBuilder<TReadEvent, TLogPos>(reader.Read);
    }

    public IEventReadBuilder<TReadEvent, TStreamPos> ReadStream(TStreamId streamId)
    {
        return new EventReadBuilder<TReadEvent, TStreamPos>(def => reader.Read(streamId, def));
    }

    public IEventSubscriptionBuilder<TReadEvent, TLogPos> SubscribeToAll(SubscriptionOptions? options)
    {
        return new EventSubscriptionBuilder<TReadEvent, TLogPos>(
            subscriber.Subscribe,
            options ?? SubscriptionOptions.Default);
    }

    public IEventSubscriptionBuilder<TReadEvent, TStreamPos> SubscribeToStream(
        TStreamId streamId,
        SubscriptionOptions? options)
    {
        return new EventSubscriptionBuilder<TReadEvent, TStreamPos>(
            def => subscriber.Subscribe(streamId, def),
            options ?? SubscriptionOptions.Default);
    }
}