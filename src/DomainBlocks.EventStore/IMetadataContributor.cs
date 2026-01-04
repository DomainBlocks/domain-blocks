namespace DomainBlocks.EventStore;

public interface IMetadataContributor<in TEvent> where TEvent : notnull
{
    void Contribute(TEvent @event, object? contract, string eventName, MetadataWriter metadata);
}