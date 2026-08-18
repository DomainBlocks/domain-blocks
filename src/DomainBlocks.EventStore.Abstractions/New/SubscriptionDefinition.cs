namespace DomainBlocks.EventStore.Abstractions.New;

public static class SubscriptionDefinition
{
    public static SubscriptionDefinition<TPos>.FromStart FromStart<TPos>()
        where TPos : notnull =>
        SubscriptionDefinition<TPos>.FromStart.Instance;

    public static SubscriptionDefinition<TPos>.FromEnd FromLive<TPos>()
        where TPos : notnull =>
        SubscriptionDefinition<TPos>.FromEnd.Instance;

    public static SubscriptionDefinition<TPos>.After After<TPos>(TPos position)
        where TPos : notnull =>
        new(position);
}

public abstract record SubscriptionDefinition<TPos> where TPos : notnull
{
    public sealed record FromStart : SubscriptionDefinition<TPos>
    {
        public static readonly FromStart Instance = new();

        private FromStart()
        {
        }
    }

    public sealed record FromEnd : SubscriptionDefinition<TPos>
    {
        public static readonly FromEnd Instance = new();

        private FromEnd()
        {
        }
    }

    public sealed record After(TPos Position) : SubscriptionDefinition<TPos>;
}