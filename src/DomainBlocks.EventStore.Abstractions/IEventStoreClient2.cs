namespace DomainBlocks.EventStore.Abstractions;

public interface IEventStoreClient2<TEvent, TStreamId, TStreamPos, TLogPos>
    where TEvent : notnull
    where TStreamId : notnull
    where TStreamPos : notnull
    where TLogPos : notnull
{
    Task AppendToStreamAsync(
        TStreamId streamId,
        ExpectedStreamState2<TStreamPos> expectedStreamState,
        IEnumerable<AppendEvent<TEvent>> events,
        AppendToStreamOptions? options = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<ReadEvent2<TEvent, TStreamId, TStreamPos, TLogPos>> ReadAll(
        ReadMode<TLogPos>? mode = null,
        ReadAllOptions? options = null);

    IAsyncEnumerable<ReadEvent2<TEvent, TStreamId, TStreamPos, TLogPos>> ReadStream(
        TStreamId streamId,
        ReadMode<TStreamPos>? mode = null,
        ReadStreamOptions? options = null);

    IAsyncEnumerable<SubscriptionMessage2<TEvent, TStreamId, TStreamPos, TLogPos>> SubscribeToAll(
        SubscribeOrigin<TLogPos>? origin = null,
        SubscribeToAllOptions? options = null);

    IAsyncEnumerable<SubscriptionMessage2<TEvent, TStreamId, TStreamPos, TLogPos>> SubscribeToStream(
        TStreamId streamId,
        SubscribeOrigin<TStreamPos>? origin = null,
        SubscribeToStreamOptions? options = null);

    IAsyncEnumerable<ReadEvent2<TEvent, TStreamId, TStreamPos, TLogPos>> ReadLog(
        ReadDefinition<TLogPos> definition);
}

// public abstract record ReadDefinition<TPos> where TPos : notnull
// {
//     public sealed record Forward : ReadDefinition<TPos>
//     {
//         public ForwardReadOrigin<TPos> Origin { get; init; } = new ForwardReadOrigin<TPos>.Start();
//         public ForwardReadMode Mode { get; init; } = new ForwardReadMode.HistoryOnly();
//     }
// }

public abstract record ForwardReadOrigin<TPos> where TPos : notnull
{
    public sealed record Start : ForwardReadOrigin<TPos>;

    public sealed record End : ForwardReadOrigin<TPos>;

    public sealed record At(TPos Position) : ForwardReadOrigin<TPos>;

    public sealed record After(TPos Position) : ForwardReadOrigin<TPos>;
}

public abstract record ForwardReadMode
{
    public sealed record HistoryOnly : ForwardReadMode;

    public sealed record HistoryThenLive(LiveSubscriberOptions? Options = null) : ForwardReadMode;
}

public sealed record LiveSubscriberOptions
{
    public int QueueCapacity { get; init; } = 1_000;
}

public static class ReadDefinition
{
    public static ReadDefinition<TPos>.Forward Forward<TPos>() where TPos : notnull
    {
        return new ReadDefinition<TPos>.Forward();
    }
}

public abstract record ReadDefinition<TPos> where TPos : notnull
{
    public sealed record Forward : ReadDefinition<TPos>
    {
        public ForwardReadOrigin<TPos> Origin { get; init; } = new ForwardReadOrigin<TPos>.Start();

        public ForwardReadMode Mode { get; init; } = new ForwardReadMode.HistoryOnly();

        public Forward FromStart() => this with
        {
            Origin = new ForwardReadOrigin<TPos>.Start()
        };

        public Forward FromEnd() => this with
        {
            Origin = new ForwardReadOrigin<TPos>.End()
        };

        public Forward From(TPos position) => this with
        {
            Origin = new ForwardReadOrigin<TPos>.At(position)
        };

        public Forward After(TPos position) => this with
        {
            Origin = new ForwardReadOrigin<TPos>.After(position)
        };

        public Forward WithLive(LiveSubscriberOptions? options = null) => this with
        {
            Mode = new ForwardReadMode.HistoryThenLive(options)
        };
    }
}