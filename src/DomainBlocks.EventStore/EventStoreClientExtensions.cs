using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore;

public static class EventStoreClientExtensions
{
    extension<TEvent, TStreamId, TStreamPos, TLogPos>(
        IEventStoreClient<TEvent, TStreamId, TStreamPos, TLogPos> client)
        where TEvent : notnull
        where TStreamId : notnull
        where TStreamPos : notnull
        where TLogPos : notnull
    {
        public Task AppendToStreamAsync(TStreamId streamId,
            ExpectedStreamState<TStreamPos> expectedState,
            IEnumerable<TEvent> events,
            AppendToStreamOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return client.AppendToStreamAsync(
                streamId,
                expectedState,
                events.Select(x => AppendEvent.Create(x)),
                null,
                options,
                cancellationToken);
        }

        public IEventReadBuilder<TEvent, TStreamId, TStreamPos, TLogPos, TLogPos>
            ReadAll(ReadAllOptions? options = null) =>
            new EventReadBuilder<TEvent, TStreamId, TStreamPos, TLogPos, TLogPos>(def => client.ReadAll(def, options));

        public IEventReadBuilder<TEvent, TStreamId, TStreamPos, TLogPos, TStreamPos> ReadStream(
            TStreamId streamId,
            ReadStreamOptions? options = null) =>
            new EventReadBuilder<TEvent, TStreamId, TStreamPos, TLogPos, TStreamPos>(def =>
                client.ReadStream(streamId, def, options));

        public IEventSubscriptionBuilder<TEvent, TLogPos> SubscribeToAll(SubscriptionOptions? options = null) =>
            new EventSubscriptionBuilder<TEvent, TLogPos>(def => client.SubscribeToAll(def, options));

        public IEventSubscriptionBuilder<TEvent, TStreamPos> SubscribeToStream(
            TStreamId streamId,
            SubscriptionOptions? options = null) =>
            new EventSubscriptionBuilder<TEvent, TStreamPos>(def =>
                client.SubscribeToStream(streamId, def, options));
    }
}