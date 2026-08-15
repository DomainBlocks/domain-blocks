namespace DomainBlocks.EventStore.Abstractions;

public sealed class SubscribeToAllOptions
{
    public static readonly SubscribeToAllOptions Default = new();

    public int LiveQueueCapacity { get; init; } = 1_000;
}