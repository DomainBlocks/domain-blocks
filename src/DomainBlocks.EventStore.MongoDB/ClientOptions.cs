namespace DomainBlocks.EventStore.MongoDB;

public sealed class ClientOptions
{
    /// <summary>
    /// Gets or sets how long an append request document survives in MongoDB before being automatically deleted.
    /// </summary>
    public TimeSpan RequestTtl { get; set; } = TimeSpan.FromSeconds(120);

    public int RequestQueueCapacity { get; set; } = 1_000;

    public int RequestBatchSize { get; set; } = 500;
}