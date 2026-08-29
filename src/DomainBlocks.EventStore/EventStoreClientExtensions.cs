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
    }
}