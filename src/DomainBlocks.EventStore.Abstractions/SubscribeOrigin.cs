namespace DomainBlocks.EventStore.Abstractions;

public static class SubscribeOrigin
{
    public static SubscribeOrigin<TPos> Start<TPos>() where TPos : notnull => SubscribeOrigin<TPos>.Start.Instance;

    public static SubscribeOrigin<TPos> End<TPos>() where TPos : notnull => SubscribeOrigin<TPos>.End.Instance;

    public static SubscribeOrigin<TPos> After<TPos>(TPos position) where TPos : notnull =>
        new SubscribeOrigin<TPos>.After(position);
}

public abstract record SubscribeOrigin<TPos> where TPos : notnull
{
    private SubscribeOrigin()
    {
    }

    public sealed record Start : SubscribeOrigin<TPos>
    {
        private Start()
        {
        }

        public static readonly Start Instance = new();
    }

    public sealed record End : SubscribeOrigin<TPos>
    {
        private End()
        {
        }

        public static readonly End Instance = new();
    }

    public sealed record After(TPos Position) : SubscribeOrigin<TPos>;
}