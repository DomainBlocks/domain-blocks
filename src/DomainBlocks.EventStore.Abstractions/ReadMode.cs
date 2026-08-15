namespace DomainBlocks.EventStore.Abstractions;

public static class ReadMode
{
    public static ReadMode<TPos> ForwardFromStart<TPos>() where TPos : notnull =>
        ReadMode<TPos>.ForwardFromStart.Instance;

    public static ReadMode<TPos> BackwardFromEnd<TPos>() where TPos : notnull =>
        ReadMode<TPos>.BackwardFromEnd.Instance;

    public static ReadMode<TPos> ForwardFrom<TPos>(TPos position, bool isInclusive = true) where TPos : notnull =>
        new ReadMode<TPos>.ForwardFrom(position, isInclusive);

    public static ReadMode<TPos> BackwardFrom<TPos>(TPos position, bool isInclusive = true) where TPos : notnull =>
        new ReadMode<TPos>.BackwardFrom(position, isInclusive);
}

public abstract record ReadMode<TPos> where TPos : notnull
{
    private ReadMode()
    {
    }

    public sealed record ForwardFromStart : ReadMode<TPos>
    {
        private ForwardFromStart()
        {
        }

        public static readonly ForwardFromStart Instance = new();
    }

    public sealed record BackwardFromEnd : ReadMode<TPos>
    {
        private BackwardFromEnd()
        {
        }

        public static readonly BackwardFromEnd Instance = new();
    }

    public sealed record ForwardFrom(TPos Position, bool IsInclusive = true) : ReadMode<TPos>;

    public sealed record BackwardFrom(TPos Position, bool IsInclusive = true) : ReadMode<TPos>;
}