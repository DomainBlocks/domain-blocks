namespace DomainBlocks.EventStore.Abstractions;

public sealed class SubscribeToStreamOptions
{
    public static readonly SubscribeToStreamOptions Default = new();

    public int LiveQueueCapacity { get; init; } = 1_000;
}