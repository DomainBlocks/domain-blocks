namespace DomainBlocks.EventStore.MongoDB;

public sealed class ClientOptions
{
    public int RequestQueueCapacity { get; set; } = 1_000;

    public int RequestBatchSize { get; set; } = 500;
}