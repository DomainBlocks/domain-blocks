using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.MongoDB;

public sealed class MongoEventStoreClient2<TEvent> :
    IEventStoreClient2<TEvent, string, StreamVersion, LogSequenceNumber>
    where TEvent : notnull
{
    public Task AppendToStreamAsync(
        string streamId,
        ExpectedStreamState2<StreamVersion> expectedStreamState,
        IEnumerable<AppendEvent<TEvent>> events,
        AppendToStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public IAsyncEnumerable<ReadEvent2<TEvent, string, StreamVersion, LogSequenceNumber>> ReadAll(
        ReadMode<LogSequenceNumber>? mode = null,
        ReadAllOptions? options = null)
    {
        throw new NotImplementedException();
    }

    public IAsyncEnumerable<ReadEvent2<TEvent, string, StreamVersion, LogSequenceNumber>> ReadStream(
        string streamId,
        ReadMode<StreamVersion>? mode = null,
        ReadStreamOptions? options = null)
    {
        throw new NotImplementedException();
    }

    public IAsyncEnumerable<SubscriptionMessage2<TEvent, string, StreamVersion, LogSequenceNumber>> SubscribeToAll(
        SubscribeOrigin<LogSequenceNumber>? origin = null,
        SubscribeToAllOptions? options = null)
    {
        throw new NotImplementedException();
    }

    public IAsyncEnumerable<SubscriptionMessage2<TEvent, string, StreamVersion, LogSequenceNumber>> SubscribeToStream(
        string streamId,
        SubscribeOrigin<StreamVersion>? origin = null,
        SubscribeToStreamOptions? options = null)
    {
        throw new NotImplementedException();
    }
}