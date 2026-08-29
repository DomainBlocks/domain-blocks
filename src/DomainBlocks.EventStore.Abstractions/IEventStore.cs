namespace DomainBlocks.EventStore.Abstractions;

public interface IEventStore<TEvent, TStreamId, TStreamPos, TLogPos>
    where TEvent : notnull
    where TStreamId : notnull
    where TStreamPos : notnull
    where TLogPos : notnull
{
    Task AppendAsync(
        TStreamId streamId,
        IEnumerable<AppendableEvent<TEvent>> events,
        ExpectedStreamState<TStreamPos>? expectedState = null,
        Guid? commitId = null,
        AppendOptions? options = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>> ReadAll(
        ReadDirection direction = ReadDirection.Forward,
        ReadOrigin<TLogPos>? origin = null,
        ReadAllOptions? options = null);

    IAsyncEnumerable<ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>> ReadStream(
        TStreamId streamId,
        ReadDirection direction = ReadDirection.Forward,
        ReadOrigin<TStreamPos>? origin = null,
        ReadStreamOptions? options = null);

    IAsyncEnumerable<SubscriptionMessage> SubscribeToAll(
        ReadOrigin<TLogPos>? origin = null,
        SubscriptionOptions? options = null);

    IAsyncEnumerable<SubscriptionMessage> SubscribeToStream(
        TStreamId streamId,
        ReadOrigin<TStreamPos>? origin = null,
        SubscriptionOptions? options = null);
}