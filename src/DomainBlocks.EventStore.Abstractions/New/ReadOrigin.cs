namespace DomainBlocks.EventStore.Abstractions.New;

public static class ReadOrigin
{
    public static ReadOrigin<TPos> Start<TPos>() where TPos : notnull => ReadOrigin<TPos>.Start.Instance;

    public static ReadOrigin<TPos> End<TPos>() where TPos : notnull => ReadOrigin<TPos>.End.Instance;

    public static ReadOrigin<TPos> From<TPos>(TPos position) where TPos : notnull =>
        new ReadOrigin<TPos>.From(position);

    public static ReadOrigin<TPos> After<TPos>(TPos position) where TPos : notnull =>
        new ReadOrigin<TPos>.After(position);
}

public abstract record ReadOrigin<TPos> where TPos : notnull
{
    public sealed record Start : ReadOrigin<TPos>
    {
        public static readonly Start Instance = new();

        private Start()
        {
        }
    }

    public sealed record End : ReadOrigin<TPos>
    {
        public static readonly End Instance = new();

        private End()
        {
        }
    }

    public sealed record From(TPos Position) : ReadOrigin<TPos>;

    public sealed record After(TPos Position) : ReadOrigin<TPos>;
}