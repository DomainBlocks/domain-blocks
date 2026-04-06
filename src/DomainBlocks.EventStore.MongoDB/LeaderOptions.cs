namespace DomainBlocks.EventStore.MongoDB;

public sealed class LeaderOptions
{
    public int QueueCapacity { get; set; } = 1_000;

    public int BatchSize { get; set; } = 500;
}