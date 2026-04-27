namespace DomainBlocks.EventStore.MongoDB;

public sealed class MongoEventStoreClient2Options
{
    public string DatabaseName { get; set; } = "domainblocks";
    public string EventLogCollectionName { get; set; } = "dbx_event_log_v2";
    public string SequenceCollectionName { get; set; } = "dbx_sequence";
    public int QueueCapacity { get; set; } = 1_000;
    public int BatchSize { get; set; } = 500;
}