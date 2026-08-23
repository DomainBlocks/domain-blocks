namespace DomainBlocks.EventStore.Abstractions;

public static class SubscriptionDefinition
{
    public static SubscriptionDefinition<TPos>.FromStart FromStart<TPos>()
        where TPos : notnull =>
        SubscriptionDefinition<TPos>.FromStart.Instance;

    public static SubscriptionDefinition<TPos>.FromLive FromLive<TPos>()
        where TPos : notnull =>
        SubscriptionDefinition<TPos>.FromLive.Instance;

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

    public sealed record FromLive : SubscriptionDefinition<TPos>
    {
        public static readonly FromLive Instance = new();

        private FromLive()
        {
        }
    }

    public sealed record After(TPos Position) : SubscriptionDefinition<TPos>;
}