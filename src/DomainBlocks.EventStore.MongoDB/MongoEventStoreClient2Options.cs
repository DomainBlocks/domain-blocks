namespace DomainBlocks.EventStore.MongoDB;

public sealed class MongoEventStoreClient2Options
{
    public string DatabaseName { get; set; } = "domainblocks";
    public string EventLogCollectionName { get; set; } = "dbx_event_log_v2";
    public string SequencesCollectionName { get; set; } = "dbx_sequences";
    public int AppendQueueCapacity { get; set; } = 1_000;
    public int AppendBatchSize { get; set; } = 500;
}