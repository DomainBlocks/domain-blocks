namespace DomainBlocks.EventStore.Abstractions.New;

public interface IEventStoreClient2<TAppendEvent, TReadEvent, TStreamId, TStreamPos, TLogPos>
    where TAppendEvent : notnull
    where TReadEvent : notnull
    where TStreamId : notnull
    where TStreamPos : notnull
    where TLogPos : notnull

{
    Task AppendToStreamAsync(
        TStreamId streamId,
        ExpectedStreamState2<TStreamPos> expectedStreamState,
        IEnumerable<TAppendEvent> events,
        AppendToStreamOptions? options = null,
        CancellationToken cancellationToken = default);

    IReadBuilder<TReadEvent, TLogPos>.IForward ReadAll();

    IReadBuilder<TReadEvent, TStreamPos>.IForward ReadStream(TStreamId streamId);
}

public interface IReadBuilder<TEvent, in TPos> where TEvent : notnull where TPos : notnull
{
    IForward Forward();

    IBackward Backward();

    interface IForward
    {
        IHistorical FromStart();
        IHistorical From(TPos position);
        IHistorical After(TPos position);
        ILive FromLive(LiveConsumerOptions? options = null);
        IBackward Backward();

        interface IHistorical
        {
            ILive ThenLive(LiveConsumerOptions? options = null);
            IAsyncEnumerable<TEvent> ToAsyncEnumerable();
        }

        interface ILive
        {
            IAsyncEnumerable<TEvent> ToAsyncEnumerable();
            ILiveNotifications AsNotifications();
        }

        interface ILiveNotifications
        {
            IAsyncEnumerable<ReadNotification<TEvent>> ToAsyncEnumerable();
        }
    }

    interface IBackward
    {
        IHistorical FromEnd();
        IHistorical From(TPos position);

        interface IHistorical
        {
            IAsyncEnumerable<TEvent> ToAsyncEnumerable();
        }
    }
}

public sealed record LiveConsumerOptions
{
    public int QueueCapacity { get; init; } = 1_000;
}