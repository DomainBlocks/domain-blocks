using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore;

public static class EventStoreExtensions
{
    extension<TEvent, TStreamId, TStreamPos, TLogPos>(
        IEventStore<TEvent, TStreamId, TStreamPos, TLogPos> eventStore)
        where TEvent : notnull
        where TStreamId : notnull
        where TStreamPos : notnull
        where TLogPos : notnull
    {
        public Task AppendAsync(
            TStreamId streamId,
            IEnumerable<TEvent> events,
            ExpectedStreamState<TStreamPos>? expectedState = null,
            Guid? commitId = null,
            AppendOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return eventStore.AppendAsync(
                streamId,
                events.Select(x => AppendableEvent.Create(x)),
                expectedState,
                commitId,
                options,
                cancellationToken);
        }
    }
}