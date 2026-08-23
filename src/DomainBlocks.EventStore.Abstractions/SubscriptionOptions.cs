namespace DomainBlocks.EventStore.Abstractions;

public sealed record SubscriptionOptions
{
    public static readonly SubscriptionOptions Default = new();

    public int QueueCapacity { get; init; } = 1_000;
}