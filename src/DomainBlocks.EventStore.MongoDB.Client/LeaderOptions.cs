namespace DomainBlocks.EventStore.MongoDB.Client;

public sealed class LeaderOptions
{
    public int CatchUpBatchSize { get; set; } = 500;

    public int LiveQueueCapacity { get; set; } = 1_000;

    public int LiveBatchSize { get; set; } = 500;
}