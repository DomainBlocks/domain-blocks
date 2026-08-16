using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.MongoDB;

public sealed class MongoEventStoreClient2<TEvent> :
    IEventStoreClient2<TEvent, string, StreamPosition, LogPosition>
    where TEvent : notnull
{
    public Task AppendToStreamAsync(
        string streamId,
        ExpectedStreamState2<StreamPosition> expectedStreamState,
        IEnumerable<AppendEvent<TEvent>> events,
        AppendToStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public IAsyncEnumerable<ReadEvent2<TEvent, string, StreamPosition, LogPosition>> ReadAll(
        ReadMode<LogPosition>? mode = null,
        ReadAllOptions? options = null)
    {
        throw new NotImplementedException();
    }

    public IAsyncEnumerable<ReadEvent2<TEvent, string, StreamPosition, LogPosition>> ReadStream(
        string streamId,
        ReadMode<StreamPosition>? mode = null,
        ReadStreamOptions? options = null)
    {
        throw new NotImplementedException();
    }

    public IAsyncEnumerable<SubscriptionMessage2<TEvent, string, StreamPosition, LogPosition>> SubscribeToAll(
        SubscribeOrigin<LogPosition>? origin = null,
        SubscribeToAllOptions? options = null)
    {
        throw new NotImplementedException();
    }

    public IAsyncEnumerable<SubscriptionMessage2<TEvent, string, StreamPosition, LogPosition>> SubscribeToStream(
        string streamId,
        SubscribeOrigin<StreamPosition>? origin = null,
        SubscribeToStreamOptions? options = null)
    {
        throw new NotImplementedException();
    }
}