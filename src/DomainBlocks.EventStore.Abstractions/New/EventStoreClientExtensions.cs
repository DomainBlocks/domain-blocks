namespace DomainBlocks.EventStore.Abstractions.New;

public static class EventStoreClientExtensions
{
    extension<TAppendEvent, TReadEvent, TStreamId, TStreamPos, TLogPos>(
        IEventStoreClient2<TAppendEvent, TReadEvent, TStreamId, TStreamPos, TLogPos> client)
        where TAppendEvent : notnull
        where TReadEvent : notnull
        where TStreamId : notnull
        where TStreamPos : notnull
        where TLogPos : notnull
    {
        public IEventReadBuilder<TReadEvent, TLogPos> ReadAll(ReadAllOptions? options = null) =>
            new EventReadBuilder<TReadEvent, TLogPos>(definition => client.ReadAll(definition, options));

        public IEventReadBuilder<TReadEvent, TStreamPos> ReadStream(
            TStreamId streamId,
            ReadStreamOptions2? options = null) =>
            new EventReadBuilder<TReadEvent, TStreamPos>(def => client.ReadStream(streamId, def, options));

        public IEventSubscriptionBuilder<TReadEvent, TLogPos> SubscribeToAll(SubscriptionOptions? options = null) =>
            new EventSubscriptionBuilder<TReadEvent, TLogPos>(def => client.SubscribeToAll(def, options));

        public IEventSubscriptionBuilder<TReadEvent, TStreamPos> SubscribeToStream(
            TStreamId streamId,
            SubscriptionOptions? options = null) =>
            new EventSubscriptionBuilder<TReadEvent, TStreamPos>(def =>
                client.SubscribeToStream(streamId, def, options));
    }
}