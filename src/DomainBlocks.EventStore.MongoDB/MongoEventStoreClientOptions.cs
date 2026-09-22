using DomainBlocks.EventStore.Filtering;

namespace DomainBlocks.EventStore.MongoDB;

public sealed class MongoEventStoreOptions
{
    public string DatabaseName { get; set; } = "domainblocks";
    public string EventLogCollectionName { get; set; } = "dbx_event_log";
    public string SequencesCollectionName { get; set; } = "dbx_sequences";
    public int AppendQueueCapacity { get; set; } = 1_000;
    public int AppendBatchSize { get; set; } = 500;

    /// <summary>
    /// Selects the events that the store's subscriptions are ever given, for an application that knows them up front.
    /// The server then leaves the rest out of the change stream, which saves their traffic as well. Every subscription
    /// is subject to it, along with its own filter. Reads are not. The default is every event.
    /// </summary>
    /// <remarks>
    /// It has to be a filter that the database can evaluate the whole of, or the store refuses it as it is built.
    /// </remarks>
    public EventFilter SubscriptionFilter
    {
        get;
        set => field = value ?? throw new ArgumentNullException(nameof(value));
    } = EventFilter.All;
}