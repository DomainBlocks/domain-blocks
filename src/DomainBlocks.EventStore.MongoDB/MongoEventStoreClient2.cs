using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Abstractions.New;

namespace DomainBlocks.EventStore.MongoDB;

public sealed class MongoEventStoreClient2<TEvent> :
    IEventStoreClient2<
        AppendEvent<TEvent>,
        ReadEvent2<TEvent, string, StreamPosition, LogPosition>,
        string,
        StreamPosition,
        LogPosition>
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

    public IReadBuilder<ReadEvent2<TEvent, string, StreamPosition, LogPosition>, LogPosition>.IForward ReadAll()
    {
        throw new NotImplementedException();
    }

    public IReadBuilder<ReadEvent2<TEvent, string, StreamPosition, LogPosition>, StreamPosition>.IForward ReadStream(
        string streamId)
    {
        throw new NotImplementedException();
    }
}