namespace DomainBlocks.EventStore.MongoDB;

public sealed class EventStoreCollectionOptions
{
    public static readonly EventStoreCollectionOptions Default = new()
    {
        DatabaseName = "domainblocks",
        EventsCollectionName = "es_events"
    };

    public required string DatabaseName { get; init; }
    public required string EventsCollectionName { get; init; }
}