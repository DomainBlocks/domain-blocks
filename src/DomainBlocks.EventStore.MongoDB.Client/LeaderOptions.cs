namespace DomainBlocks.EventStore.MongoDB.Client;

public sealed class LeaderOptions
{
    public int CatchUpBatchSize { get; set; } = 1000;

    public int LiveQueueCapacity { get; set; } = 1000;

    public int LiveBatchSize { get; set; } = 1000;
}