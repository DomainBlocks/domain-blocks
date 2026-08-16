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
}

// public abstract record ReadDefinition<TPos> where TPos : notnull
// {
//     public sealed record Forward : ReadDefinition<TPos>
//     {
//         public ForwardReadOrigin<TPos> Origin { get; init; } = new ForwardReadOrigin<TPos>.Start();
//         public ForwardReadMode Mode { get; init; } = new ForwardReadMode.HistoryOnly();
//     }
// }
/*
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
*/

// (Forward) history only (start, from, after)
// (Forward) history then live (start, from, after)
// (Forward) live only (end)
// (Backward) history (end, from)

// Historical events are events already present in the log; live events are events observed after the read has reached
// the current end of the log.

public static class ReadDefinition
{
    public static ForwardReadDefinitionBuilder<TPos> Forward<TPos>()
        where TPos : notnull
        => new();

    public static BackwardReadDefinitionBuilder<TPos> Backward<TPos>()
        where TPos : notnull
        => new();
}

// ─────────────────────────────────────────────────────────────
// Read definitions
// ─────────────────────────────────────────────────────────────

public abstract record ReadDefinition<TPos>
    where TPos : notnull
{
    public abstract record Forward : ReadDefinition<TPos>
    {
        public sealed record Historical(
            ForwardReadOrigin<TPos> Origin)
            : Forward
        {
            public HistoricalThenLive ThenLive(
                LiveConsumerOptions? options = null)
                => new(Origin, options);
        }

        public sealed record HistoricalThenLive(
            ForwardReadOrigin<TPos> Origin,
            LiveConsumerOptions Options)
            : Forward;

        public sealed record Live(
            LiveConsumerOptions Options)
            : Forward;
    }

    public abstract record Backward : ReadDefinition<TPos>
    {
        public sealed record Historical(
            BackwardReadOrigin<TPos> Origin)
            : Backward;
    }
}

// ─────────────────────────────────────────────────────────────
// Forward builder
// ─────────────────────────────────────────────────────────────

public sealed class ForwardReadDefinitionBuilder<TPos>
    where TPos : notnull
{
    public ReadDefinition<TPos>.Forward.Historical FromStart()
        => new(new ForwardReadOrigin<TPos>.Start());

    public ReadDefinition<TPos>.Forward.Historical From(TPos position)
        => new(new ForwardReadOrigin<TPos>.At(position));

    public ReadDefinition<TPos>.Forward.Historical After(TPos position)
        => new(new ForwardReadOrigin<TPos>.After(position));

    public ReadDefinition<TPos>.Forward.Live FromLive(
        LiveConsumerOptions? options = null)
        => new(options ?? new LiveConsumerOptions());
}

// ─────────────────────────────────────────────────────────────
// Backward builder
// ─────────────────────────────────────────────────────────────

public sealed class BackwardReadDefinitionBuilder<TPos>
    where TPos : notnull
{
    public ReadDefinition<TPos>.Backward.Historical FromEnd()
        => new(new BackwardReadOrigin<TPos>.End());

    public ReadDefinition<TPos>.Backward.Historical From(TPos position)
        => new(new BackwardReadOrigin<TPos>.At(position));
}

// ─────────────────────────────────────────────────────────────
// Origins
// ─────────────────────────────────────────────────────────────

public abstract record ForwardReadOrigin<TPos>
    where TPos : notnull
{
    public sealed record Start : ForwardReadOrigin<TPos>;

    public sealed record At(TPos Position) : ForwardReadOrigin<TPos>;

    public sealed record After(TPos Position) : ForwardReadOrigin<TPos>;
}

public abstract record BackwardReadOrigin<TPos>
    where TPos : notnull
{
    public sealed record End : BackwardReadOrigin<TPos>;

    public sealed record At(TPos Position) : BackwardReadOrigin<TPos>;
}

// ─────────────────────────────────────────────────────────────
// Live options
// ─────────────────────────────────────────────────────────────

public sealed record LiveConsumerOptions
{
    public int QueueCapacity { get; init; } = 1_000;
}