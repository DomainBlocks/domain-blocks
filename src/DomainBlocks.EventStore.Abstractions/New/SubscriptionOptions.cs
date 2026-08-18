namespace DomainBlocks.EventStore.Abstractions.New;

public sealed record SubscriptionOptions
{
    public static readonly SubscriptionOptions Default = new();

    public int QueueCapacity { get; init; } = 1_000;
}