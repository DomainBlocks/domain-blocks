namespace DomainBlocks.EventStore.Abstractions;

public static class ReadOrigin
{
    public static ReadOrigin<TPos>.Start Start<TPos>() where TPos : notnull => ReadOrigin<TPos>.Start.Instance;

    public static ReadOrigin<TPos>.End End<TPos>() where TPos : notnull => ReadOrigin<TPos>.End.Instance;

    public static ReadOrigin<TPos>.Position Position<TPos>(TPos value) where TPos : notnull => new(value);
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

    public sealed record Position(TPos Value) : ReadOrigin<TPos>;
}