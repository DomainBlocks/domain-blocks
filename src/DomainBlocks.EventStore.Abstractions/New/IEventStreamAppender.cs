namespace DomainBlocks.EventStore.Abstractions.New;

public interface IEventStreamAppender<in TAppendEvent, in TStreamId, TStreamPos> where TStreamPos : notnull
{
    Task AppendAsync(
        TStreamId streamId,
        ExpectedStreamState2<TStreamPos> expectedState,
        IEnumerable<TAppendEvent> events,
        AppendToStreamOptions? options = null,
        CancellationToken cancellationToken = default);
}