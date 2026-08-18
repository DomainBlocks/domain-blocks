namespace DomainBlocks.EventStore.Abstractions.New;

public static class ReadDefinition
{
    public static ReadDefinition<TPos>.ForwardFromStart ForwardFromStart<TPos>()
        where TPos : notnull
        => ReadDefinition<TPos>.ForwardFromStart.Instance;

    public static ReadDefinition<TPos>.ForwardFrom ForwardFrom<TPos>(TPos position)
        where TPos : notnull
        => new(position);

    public static ReadDefinition<TPos>.BackwardFromEnd BackwardFromEnd<TPos>()
        where TPos : notnull
        => ReadDefinition<TPos>.BackwardFromEnd.Instance;

    public static ReadDefinition<TPos>.BackwardFrom BackwardFrom<TPos>(TPos position)
        where TPos : notnull
        => new(position);
}

public abstract record ReadDefinition<TPos> where TPos : notnull
{
    public sealed record ForwardFromStart : ReadDefinition<TPos>
    {
        public static readonly ForwardFromStart Instance = new();

        private ForwardFromStart()
        {
        }
    }

    public sealed record ForwardFrom(TPos Position) : ReadDefinition<TPos>;

    public sealed record BackwardFromEnd : ReadDefinition<TPos>
    {
        public static readonly BackwardFromEnd Instance = new();

        private BackwardFromEnd()
        {
        }
    }

    public sealed record BackwardFrom(TPos Position) : ReadDefinition<TPos>;
}